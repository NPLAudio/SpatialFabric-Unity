"""Offline unit tests; no Unity execution, player build, or release artifact changes."""
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import player_notices as notices


class NoticeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.project = self.root / 'ExampleProject'
        self.editor = self.root / 'Editor'
        self.cache = self.root / 'official-cache'
        self.output = self.root / 'player'
        self.output.mkdir()
        self.builtin = self.editor / 'Data/Resources/PackageManager/BuiltInPackages'
        self.resolved = self.project / 'Library/PackageCache'
        self.lock = {'dependencies': {}}
        package_metadata = {}
        packages = {}
        self.sources = {}
        for package, (version, docs) in notices.PACKAGES.items():
            builtin = package in ('com.unity.ugui', 'com.unity.mathematics')
            source = self.builtin / package if builtin else self.resolved / (package + '@resolved')
            self.sources[package] = source
            metadata = json.dumps({'name': package, 'version': version}).encode()
            self.put(source / 'package.json', metadata)
            package_metadata[package] = notices.digest(metadata)
            packages[package] = (version, {})
            for filename in docs:
                data = (package + filename).encode()
                self.put(source / filename, data)
                packages[package][1][filename] = notices.digest(data)
            # Deliberately present private/extra content; it must never be copied.
            self.put(source / 'unity_license.json', b'SECRET')
            self.put(source / 'extra.txt', b'not a notice')
            if not builtin:
                self.lock['dependencies'][package] = {'version': version, 'source': 'builtin'}
        for filename in notices.EDITOR_FILES:
            self.put(self.editor / filename, filename.encode())
        for name in notices.DOCUMENTS:
            self.put(self.cache / name, name.encode())
        for filename in notices.BURST_NOTICES:
            self.put(self.editor / 'Data/Tools/Burst' / filename, filename.encode())
        self.put(self.project / 'Packages/packages-lock.json', json.dumps(self.lock).encode())
        self.put(self.project / 'Packages/manifest.json', b'{}\n')
        self.put(self.project / 'ProjectSettings/ProjectVersion.txt', b'm_EditorVersion: 6000.6.9f1\n')
        # Use the actual project license (LF), whose pin is deliberately not mocked.
        self.put(self.root / 'LICENSE.md', (Path(__file__).resolve().parents[1] / 'LICENSE.md').read_bytes().replace(b'\r\n', b'\n'))
        self.inputs = {relative: notices.digest((self.project / relative).read_bytes()) for relative in notices.INPUTS}
        replacements = dict(PACKAGES=packages, PACKAGE_METADATA=package_metadata, INPUTS=self.inputs,
                            EDITOR_FILES={name: notices.digest(name.encode()) for name in notices.EDITOR_FILES},
                            BURST_NOTICES={name: notices.digest(name.encode()) for name in notices.BURST_NOTICES},
                            DOCUMENTS={name: (url, notices.digest(name.encode())) for name, (url, _) in notices.DOCUMENTS.items()})
        for name, value in replacements.items():
            mock = patch.object(notices, name, value)
            mock.start()
            self.addCleanup(mock.stop)

    def put(self, path, data):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)

    def collect(self):
        return notices.collect_notices(self.root, self.editor / 'Unity.exe', self.cache)

    def update_lock(self):
        data = json.dumps(self.lock).encode()
        self.put(self.project / 'Packages/packages-lock.json', data)
        self.inputs['Packages/packages-lock.json'] = notices.digest(data)

    def test_roundtrip_only_allowlisted_documents_and_hashes(self):
        files, manifest = self.collect()
        self.assertEqual(15, len(files))
        self.assertEqual('verified_allowlisted_inputs_only', manifest['status'])
        self.assertFalse(manifest['completeness_verified'])
        self.assertTrue(manifest['limitations'])
        notices.write_notices(self.output, files, manifest)
        paths = notices.archive_files(self.output)
        self.assertEqual(16, len(paths))
        self.assertFalse(any(b'SECRET' in path.read_bytes() for path in paths))
        for entry in manifest['files']:
            self.assertEqual(entry['sha256'], notices.digest(files[entry['path']]))
        ugui = [e for e in manifest['files'] if e.get('package') == 'com.unity.ugui'][0]
        self.assertEqual('conservative-builtin', ugui['resolution'])

    def test_missing_or_modified_mandatory_document(self):
        source = self.sources['com.unity.render-pipelines.core'] / 'THIRD PARTY NOTICES.md'
        source.write_bytes(b'tampered')
        with self.assertRaisesRegex(ValueError, 'SHA256 mismatch'):
            self.collect()
        source.unlink()
        with self.assertRaisesRegex(ValueError, 'Missing/unsafe'):
            self.collect()

    def test_missing_burst_notice_fails(self):
        (self.editor / 'Data/Tools/Burst/LICENSE.md').unlink()
        with self.assertRaisesRegex(ValueError, 'Missing/unsafe'):
            self.collect()

    def test_companion_body_ignores_site_chrome_not_license_edits(self):
        first = b'<html nonce="a"><header>old</header><main><h1>License</h1><p>Full <b>legal</b> text.</p><a href="https://unity.com">terms</a></main></html>'
        second = first.replace(b'nonce="a"', b'nonce="b"').replace(b'>old<', b'>new<')
        canonical = notices.companion_text(first)
        self.assertEqual(canonical, notices.companion_text(second))
        self.assertIn(b'Full legal text.', canonical)
        self.assertIn(b'https://unity.com', canonical)
        self.assertNotEqual(canonical, notices.companion_text(first.replace(b'legal', b'changed')))
        with self.assertRaisesRegex(ValueError, 'one complete'):
            notices.companion_text(b'<html>No legal main body</html>')

    def test_package_metadata_change_fails(self):
        path = self.sources['com.unity.ugui'] / 'package.json'
        path.write_bytes(path.read_bytes() + b' ')
        with self.assertRaisesRegex(ValueError, 'SHA256 mismatch'):
            self.collect()

    def test_changed_resolution_and_unknown_packages_fail(self):
        self.lock['dependencies']['unknown.runtime'] = {'version': '1.0'}
        self.update_lock()
        with self.assertRaisesRegex(ValueError, 'Unreviewed packages'):
            self.collect()
        del self.lock['dependencies']['unknown.runtime']
        self.lock['dependencies']['com.unity.render-pipelines.core']['version'] = '99'
        self.update_lock()
        with self.assertRaisesRegex(ValueError, 'Unreviewed resolved version'):
            self.collect()

    def test_input_change_without_pin_review_fails(self):
        (self.project / 'Packages/manifest.json').write_bytes(b'{"changed": true}')
        with self.assertRaisesRegex(ValueError, 'SHA256 mismatch'):
            self.collect()

    def test_missing_and_ambiguous_cache_fail_no_builtin_fallback(self):
        source = self.sources['com.unity.render-pipelines.core']
        source.rename(source.with_name(source.name + '-other'))
        source.mkdir()
        with self.assertRaisesRegex(ValueError, 'ambiguous resolved package'):
            self.collect()
        source.rmdir()
        source.with_name(source.name + '-other').rename(self.root / 'elsewhere')
        with self.assertRaisesRegex(ValueError, 'Missing/ambiguous'):
            self.collect()

    def test_official_hash_missing_and_editor_identity_fail(self):
        doc = self.cache / next(iter(notices.DOCUMENTS))
        original = doc.read_bytes()
        doc.write_bytes(b'HTML error page, not the required PDF')
        with self.assertRaisesRegex(ValueError, 'SHA256 mismatch'):
            self.collect()
        doc.unlink()
        with self.assertRaisesRegex(ValueError, 'Missing/unsafe'):
            self.collect()
        doc.write_bytes(original)
        (self.editor / 'Unity.dll').write_bytes(b'wrong editor')
        with self.assertRaisesRegex(ValueError, 'SHA256 mismatch'):
            self.collect()

    def test_no_network_fallback_for_incomplete_offline_cache(self):
        (self.cache / next(iter(notices.DOCUMENTS))).unlink()
        with patch('urllib.request.urlopen') as request:
            with self.assertRaises(ValueError):
                self.collect()
            request.assert_not_called()

    def test_bundle_tampering_and_extra_files_fail(self):
        files, manifest = self.collect()
        notices.write_notices(self.output, files, manifest)
        extra = self.output / 'ThirdPartyNotices/extra.txt'
        extra.write_bytes(b'no')
        with self.assertRaisesRegex(ValueError, 'Unexpected/missing'):
            notices.archive_files(self.output)
        extra.unlink()
        (self.output / 'ThirdPartyNotices' / next(iter(files))).write_bytes(b'tampered')
        with self.assertRaisesRegex(ValueError, 'SHA256 mismatch'):
            notices.archive_files(self.output)

    def test_backups_removed_before_zip_and_activation_rejected(self):
        files, manifest = self.collect()
        notices.write_notices(self.output, files, manifest)
        for parent in (self.output, self.output / 'nested'):
            for name in (notices.BACKUP, 'SpatialFabricRoomShowcase_' + notices.BACKUP):
                self.put(parent / name / 'private.pdb', b'dont ship')
        self.put(self.output / 'keep.txt', b'keep')
        notices.remove_player_backups(self.output)
        archive = self.root / 'test.zip'
        with zipfile.ZipFile(archive, 'w') as z:
            for path in notices.archive_files(self.output):
                z.write(path, path.relative_to(self.output))
        with zipfile.ZipFile(archive) as z:
            self.assertIn('keep.txt', z.namelist())
            self.assertFalse(any(notices.BACKUP in p for p in z.namelist()))
        for name in ('unity_license.json', 'secret.ULF', notices.BACKUP, 'SpatialFabricRoomShowcase_' + notices.BACKUP):
            path = self.output / name
            path.write_bytes(b'forbidden')
            with self.assertRaisesRegex(ValueError, 'Forbidden release output'):
                notices.archive_files(self.output)
            path.unlink()

    def test_unsafe_paths_rejected(self):
        with self.assertRaisesRegex(ValueError, 'Outside approved root'):
            notices.safe_file(self.root / 'LICENSE.md', self.cache)
        files, manifest = self.collect()
        manifest['files'][0]['path'] = '../escape'
        with self.assertRaisesRegex(ValueError, 'Unsafe/duplicate'):
            notices.write_notices(self.output, files, manifest)

    def test_download_checks_bytes_and_https(self):
        name = next(iter(notices.DOCUMENTS))
        from unittest.mock import MagicMock
        response = MagicMock()
        response.__enter__.return_value = response
        response.geturl.return_value = 'https://official.example/notice'
        response.read.return_value = name.encode()
        with patch('urllib.request.urlopen', return_value=response):
            self.assertEqual(name.encode(), notices.official_document(name))
            response.read.return_value = b'wrong bytes'
            with self.assertRaisesRegex(ValueError, 'SHA256 mismatch'):
                notices.official_document(name)
            response.geturl.return_value = 'http://insecure.example/'
            with self.assertRaisesRegex(ValueError, 'off HTTPS'):
                notices.official_document(name)

    def test_symlink_cannot_escape_or_be_archived(self):
        link = self.cache / 'linked.txt'
        try:
            link.symlink_to(self.root / 'LICENSE.md')
        except OSError:
            self.skipTest('Symlink creation not permitted on this host')
        with self.assertRaisesRegex(ValueError, 'Linked notice input'):
            notices.safe_file(link, self.cache)
        output_link = self.output / notices.BACKUP
        output_link.symlink_to(self.cache, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, 'Linked release output'):
            notices.remove_player_backups(self.output)
        self.assertTrue((self.root / 'LICENSE.md').is_file())
        with self.assertRaisesRegex(ValueError, 'Linked release output'):
            notices.archive_files(self.output)

    def test_build_manifest_has_git_source_and_license_anchor(self):
        # Integration seam without launching Unity/building a player.
        text = (Path(__file__).parent / 'build_player.py').read_text()
        self.assertIn("'rev-parse', '--verify', 'HEAD'", text)
        self.assertIn("'source_commit': source_commit", text)
        self.assertIn("'sha256': sha(notice_path)", text)
        self.assertLess(text.index('remove_player_backups(folder)'), text.index('zipfile.ZipFile(archive'))


if __name__ == '__main__':
    unittest.main()
