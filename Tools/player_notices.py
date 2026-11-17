"""Narrow, hash-pinned notice inputs for the Unity 6000.6.9f1 Windows Mono player.

This verifies the reviewed notice set, not whole-application legal compliance.
Never searches Editor licensing/activation directories or copies package trees.
"""
import hashlib
import json
import shutil
import urllib.request
from html.parser import HTMLParser
from pathlib import Path

UNITY = '6000.6.9f1'
BACKUP = 'BackUpThisFolder_ButDontShipItWithYourGame'
BACKUP_NAMES = {BACKUP.casefold(), ('SpatialFabricRoomShowcase_' + BACKUP).casefold()}
EDITOR_FILES = {
    'Unity.exe': '1b93875bba4ec9f0b1d16328558efd5e0d3456a1a46bafc9c2f627a648a95f57',
    'Unity.dll': '743cae04bafc314490fe2bb5310a5cbfa808fb362d9ea855956827f9f0fa8dff',
}
RELEASE_URL = 'https://unity.com/releases/editor/whats-new/6000.6.9f1'
# Published in the release page's Third Party Notices section, for this backend only.
DOCUMENTS = {
    'Unity-Player-Windows-Mono-6000.6.9f1.pdf': (
        'https://storage.googleapis.com/live-platform-resources-prd/templates/assets/Player_Windows_Mono_6000_6_9f1_b37ee8471b/Player_Windows_Mono_6000_6_9f1_b37ee8471b.pdf',
        'e7cb5f9c3276dfc9e44e79fdceca3f42c82d7a5f81dcee4cc1012426e71af48a'),
    'Unity-Companion-License.txt': (
        'https://unity.com/legal/licenses/unity-companion-license',
        '3450c723ba0f0d85250f2e8f867bd79d2fb2d662f9a0f77cbf349154939ebe60'),
}
BURST_NOTICES = {
    'LICENSE.md': 'ac0d4bd5921b5683770ebc4dc95f0ee392a1bed5df14dc65667a27e3fb7d1f6d',
    'Third Party Notices.md': '01b366462da89d104fc7db01309b69de07cee05400ec429e2ea9e4c23fa5f085',
}
# Exact filenames (including case), resolved versions and installed notice bytes.
PACKAGES = {
    'com.unity.render-pipelines.universal': ('17.6.0', {
        'LICENSE.md': '4be520a425e06c8de56fa8aee1ce957cfe610406456278172a2d18b7c388c2cf',
        'Third Party Notices.md': '0652509dfc81a1a3d9e037e59bd047b305155fc5c4abeef69e2fc078488035fd'}),
    'com.unity.render-pipelines.core': ('17.6.0', {
        'LICENSE.md': '39691adc5fdaf614d951f53637e22501baf46fcc535a69ab8a21ebaa8b9e9cb4',
        'THIRD PARTY NOTICES.md': 'fdfacc9a242bda05b26eace324033b467b7d70e83a2761cd03337b612affc339'}),
    'com.unity.render-pipelines.universal-config': ('17.6.0', {
        'LICENSE.md': 'fa2dcbc8ff967f49342c6c61854909ba4bbcc70b27f07d25538b8eaf7be5be3c'}),
    'com.unity.collections': ('6.6.0', {
        'LICENSE.md': '4947f647c031b0fec440237f8136386a606dd998089bb1df67316ee693f568f5'}),
    'com.unity.profiling.core': ('1.0.3', {
        'LICENSE.md': '33197414e7362ae534aeeab9ab6a9dfe0b5ca74b04f304805f592bcf667a5a99'}),
    'com.unity.shadergraph': ('17.6.0', {
        'LICENSE.md': '364b0c08b7fe15b70c4777dccb26fc21d02ae831c2f707dbe29879ac15ece353'}),
    'com.unity.ugui': ('2.6.0', {
        'LICENSE.md': '3f8833f9736c0b5db5076663ba5abb0a33606712fa9128456ac834fc9d43fdd6'}),
    'com.unity.mathematics': ('1.4.0', {
        'LICENSE.md': '5b1a26ff2ca6e6e7d9fd2b6df151e1a43c671fdf49c05dcee1227f20dcf25fde'}),
}
PACKAGE_METADATA = {
    'com.unity.render-pipelines.universal': '28970f261596ab239da596e45f83bf7d0b435eef84b21c90c6df624d8f3419d7',
    'com.unity.render-pipelines.core': '06f8b626fa533dd55a2f6d2a1397d538368cccb2a5ced359909db9a8f658b39a',
    'com.unity.render-pipelines.universal-config': 'e668ed27da1946399343c5dbf70aca4c0c90a61502d63bad59974f1e023160f3',
    'com.unity.collections': '4a0abb41486bf7ef49d49227b189800a1e701d428d6cf826c9736b5730b0c7d5',
    'com.unity.profiling.core': '2b14f52db262b86982ee8e3f3d03b2e2231edb4f250fd50e916b1ae1a62d5b5a',
    'com.unity.shadergraph': 'a4901bc739fa8451460563133a7fc17e34bf6d6001ffce58279069be89f9cbec',
    'com.unity.ugui': '817628b5ad7fd255e49b12d168872ba0904fcee1cf6d5e58ed78b1c0e9acdf6e',
    'com.unity.mathematics': 'bc52517ecf129a24f106a8dabd8317d0e8173d0972ff410112e92f4af5ce3656',
}
# Pin the inspected resolution, including excluded editor/test dependencies.
INPUTS = {
    'Packages/manifest.json': 'cc256ba4ddfa93c1860d4cfdb27fdd7a208dea2d8fd8ddd1610aa3a3ade96fe8',
    'Packages/packages-lock.json': 'c99055ff0cecfa3d15ef20ee39d95cfc578beabb7e1c20c88e1ab5811cb09733',
    'ProjectSettings/ProjectVersion.txt': 'ebaccfd103d4983934b050d2ef8652fcbc3fd5eade2304268930fc6e4dfccf84',
}
EXCLUDED = ['com.unity.graph-authoring', 'com.unity.searcher', 'com.unity.nuget.mono-cecil',
            'com.unity.ext.nunit', 'com.unity.test-framework', 'com.unity.test-framework.performance']
ENGINE_COMPONENTS = ['com.unity.burst', 'com.unity.modules.animation', 'com.unity.modules.audio',
                     'com.unity.modules.imageconversion', 'com.unity.modules.imgui',
                     'com.unity.modules.jsonserialize', 'com.unity.modules.physics',
                     'com.unity.modules.ui', 'com.unity.modules.physics2d', 'com.unity.modules.physicscore2d']
LIMITATIONS = [
    'Verified allowlisted notice bytes only; complete distribution compliance is not asserted.',
    'The matching Windows Mono Player TPN covers listed engine components. The exact Editor Burst license '
    'and full third-party notices are conservatively included for generated Burst code; these texts do not '
    'independently prove the generated DLL contents or LLVM version. Changed dependencies require review.',
    'UGUI and Mathematics are conservative builtin inclusions, not lockfile-resolved dependencies.',
    'Editor/test packages are excluded from this notice set, not proven absent from the player.',
    'Imported samples/fonts/assets and any changed native dependencies require separate review. '
    'SpatialFabricAudio/sf_audio is project MIT plus OS system APIs; no extra native dependency is assumed.',
]


def digest(data):
    return hashlib.sha256(data).hexdigest()


def checked(data, expected, label):
    if digest(data) != expected:
        raise ValueError(f'Notice input SHA256 mismatch: {label}; review required')
    return data


def safe_file(path, root):
    """Reject redirects outside the explicitly approved root, including symlinks."""
    root = root.absolute()
    path = path.absolute()
    if not path.is_relative_to(root):
        raise ValueError(f'Outside approved root: {path}')
    for part in (path, *path.parents):
        if part.is_symlink() or (hasattr(part, 'is_junction') and part.is_junction()):
            raise ValueError(f'Linked notice input: {part}')
        if part == root:
            break
    if not path.resolve().is_relative_to(root.resolve()) or not path.is_file():
        raise ValueError(f'Missing/unsafe notice input: {path}')
    return path.read_bytes()


def companion_text(data):
    """Preserve the full legal main body and links, excluding volatile site chrome."""
    class MainText(HTMLParser):
        def __init__(self):
            super().__init__(convert_charrefs=True)
            self.active = False
            self.seen = 0
            self.skip = 0
            self.parts = []

        def handle_starttag(self, tag, attrs):
            if tag == 'main':
                self.seen += 1
                self.active = True
            elif self.active:
                if tag in ('script', 'style'):
                    self.skip += 1
                elif not self.skip and tag == 'a':
                    href = dict(attrs).get('href', '')
                    if href:
                        self.parts.append(f' ({href}) ')

        def handle_endtag(self, tag):
            if tag == 'main':
                self.active = False
            elif self.active:
                if tag in ('script', 'style'):
                    self.skip = max(0, self.skip - 1)
                elif not self.skip and tag in ('p', 'h1', 'h2', 'h3', 'li', 'br'):
                    self.parts.append('\n')

        def handle_data(self, text):
            if self.active and not self.skip:
                self.parts.append(text)

    parser = MainText()
    parser.feed(data.decode('utf-8'))
    parser.close()
    if parser.seen != 1 or parser.active:
        raise ValueError('Expected one complete official Companion License main body')
    lines = [' '.join(line.split()) for line in ''.join(parser.parts).splitlines()]
    return ('\n'.join(line for line in lines if line) + '\n').encode('utf-8')


def official_document(name, cache=None):
    url, expected = DOCUMENTS[name]
    if cache is not None:
        data = safe_file(Path(cache) / name, Path(cache))
    else:
        with urllib.request.urlopen(url, timeout=60) as response:
            if not response.geturl().startswith('https://'):
                raise ValueError('Official notice redirected off HTTPS')
            data = response.read(2_000_001)
        if len(data) > 2_000_000:
            raise ValueError('Oversized official notice')
        if name == 'Unity-Companion-License.txt':
            data = companion_text(data)
    return checked(data, expected, url)


def collect_notices(root, unity, cache=None):
    """Preflight before Unity starts; return only reviewed bytes and provenance."""
    project = Path(root) / 'ExampleProject'
    editor = Path(unity).parent
    builtin = editor / 'Data/Resources/PackageManager/BuiltInPackages'
    if Path(unity).name.casefold() != 'unity.exe':
        raise ValueError('Expected the reviewed Windows Unity.exe')
    for filename, expected in EDITOR_FILES.items():
        checked(safe_file(editor / filename, editor), expected, f'Unity {UNITY} {filename}')
    inputs = {}
    for relative, expected in INPUTS.items():
        # Git checkout line endings are not a dependency change. Pins are LF-normalized.
        inputs[relative] = checked(safe_file(project / relative, project).replace(b'\r\n', b'\n'), expected, relative)
    lock = json.loads(inputs['Packages/packages-lock.json'])['dependencies']
    known = set(PACKAGES) | set(EXCLUDED) | set(ENGINE_COMPONENTS) | {'com.nplaudio.spatialfabric'}
    if set(lock) - known:
        raise ValueError(f'Unreviewed packages: {sorted(set(lock) - known)}')
    files, entries = {}, []

    def add(relative, data, source, **metadata):
        files[relative] = data
        entries.append(dict(path=relative, bytes=len(data), sha256=digest(data), source=source, **metadata))

    for name, (url, _) in DOCUMENTS.items():
        add(name, official_document(name, cache), url)
    for filename, expected in BURST_NOTICES.items():
        relative = 'Data/Tools/Burst/' + filename
        data = checked(safe_file(editor / relative, editor), expected, relative)
        add('Unity-Burst/' + filename, data, 'UnityEditor/' + relative)
    for package, (version, notices) in PACKAGES.items():
        if package in lock:
            if lock[package]['version'] != version:
                raise ValueError(f'Unreviewed resolved version: {package}')
            candidates = sorted((project / 'Library/PackageCache').glob(package + '@*'))
            plain = project / 'Library/PackageCache' / package
            if plain.exists():
                candidates.append(plain)
            if len(candidates) != 1:
                raise ValueError(f'Missing/ambiguous resolved package: {package}: {candidates}')
            source = candidates[0]
            approved = project / 'Library/PackageCache'
        else:
            if package not in ('com.unity.ugui', 'com.unity.mathematics'):
                raise ValueError(f'Missing resolved package: {package}')
            source, approved = builtin / package, builtin
        metadata = checked(safe_file(source / 'package.json', approved), PACKAGE_METADATA[package],
                           str(source / 'package.json'))
        identity = json.loads(metadata)
        if (identity.get('name'), identity.get('version')) != (package, version):
            raise ValueError(f'Package identity mismatch: {source}')
        for filename, expected in notices.items():
            data = checked(safe_file(source / filename, approved), expected, str(source / filename))
            source_label = ('ExampleProject/Library/PackageCache/' if package in lock else
                            'UnityEditor/Data/Resources/PackageManager/BuiltInPackages/')
            add(f'{package}/{filename}', data, source_label + (source / filename).relative_to(approved).as_posix(), package=package,
                version=version, resolution='lockfile' if package in lock else 'conservative-builtin',
                package_json_sha256=digest(metadata))
    mit = checked(safe_file(Path(root) / 'LICENSE.md', Path(root)).replace(b'\r\n', b'\n'),
                  '1b29f7747dacfbf80c2ca627cbb34a10e835410e008c7123d5b676bef2d22893', 'project MIT')
    add('SpatialFabric-MIT.md', mit, 'LICENSE.md')
    return files, dict(schema_version=1, unity=UNITY, platform='Windows-x64', backend='Mono',
                       status='verified_allowlisted_inputs_only', completeness_verified=False,
                       release_source=RELEASE_URL,
                       inputs_lf_normalized_sha256={k: digest(v) for k, v in inputs.items()},
                       editor_binary_sha256=EDITOR_FILES, files=entries,
                       excluded_editor_test_packages=EXCLUDED, engine_provided_components=ENGINE_COMPONENTS,
                       limitations=LIMITATIONS)


def write_notices(folder, files, manifest):
    target = Path(folder) / 'ThirdPartyNotices'
    if target.exists():
        raise ValueError(f'Notice destination already exists: {target}')
    target.mkdir()
    for relative, data in files.items():
        if Path(relative).is_absolute() or '..' in Path(relative).parts or '\\' in relative or ':' in relative:
            raise ValueError(f'Unsafe notice destination: {relative}')
        dest = target / relative
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(data)
    path = target / 'LICENSE_BUNDLE_MANIFEST.json'
    path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    verify_notices(folder)
    return path


def verify_notices(folder):
    target = Path(folder) / 'ThirdPartyNotices'
    manifest_path = target / 'LICENSE_BUNDLE_MANIFEST.json'
    manifest = json.loads(safe_file(manifest_path, target))
    expected = {'LICENSE_BUNDLE_MANIFEST.json'}
    for entry in manifest['files']:
        relative = entry['path']
        if (relative in expected or Path(relative).is_absolute() or '..' in Path(relative).parts
                or '\\' in relative or ':' in relative):
            raise ValueError(f'Unsafe/duplicate notice entry: {relative}')
        expected.add(relative)
        data = checked(safe_file(target / relative, target), entry['sha256'], relative)
        if len(data) != entry['bytes']:
            raise ValueError(f'Notice length mismatch: {relative}')
    actual = {p.relative_to(target).as_posix() for p in target.rglob('*') if p.is_file()}
    if actual != expected:
        raise ValueError('Unexpected/missing files in license bundle')


def remove_player_backups(folder):
    # Delete only Unity's exact backup directory, never arbitrary matching files.
    folder = Path(folder)
    paths = [folder, *folder.rglob('*')]
    for path in paths:
        if path.is_symlink() or (hasattr(path, 'is_junction') and path.is_junction()):
            raise ValueError(f'Linked release output: {path}')
    for path in sorted(paths[1:], key=lambda p: len(p.parts), reverse=True):
        if path.name.casefold() in BACKUP_NAMES:
            if path.is_symlink() or (hasattr(path, 'is_junction') and path.is_junction()) or not path.is_dir():
                raise ValueError(f'Unsafe backup directory: {path}')
            shutil.rmtree(path)


def archive_files(folder):
    """Fail closed before ZIP creation; never follow links or ship activation data."""
    folder = Path(folder)
    if folder.is_symlink() or (hasattr(folder, 'is_junction') and folder.is_junction()):
        raise ValueError(f'Linked release output: {folder}')
    files = []
    for path in sorted(folder.rglob('*')):
        if path.is_symlink() or (hasattr(path, 'is_junction') and path.is_junction()):
            raise ValueError(f'Linked release output: {path}')
        if (path.name.casefold() in BACKUP_NAMES | {'unity_license.json'}
                or path.suffix.casefold() == '.ulf'):
            raise ValueError(f'Forbidden release output: {path}')
        if path.is_file():
            files.append(path)
    verify_notices(folder)
    return files
