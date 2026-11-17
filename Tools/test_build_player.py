"""Player release gates without Unity, audio or builds."""
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import build_player as builder


class ReleaseGateTests(unittest.TestCase):
    def test_versions_cannot_escape_output(self):
        self.assertIn('0.6.0', builder.release_name('0.6.0'))
        for version in ('../0.6.0', '0.6.0/../../escape', 'C:\\escape', '', None):
            with self.assertRaises(ValueError):
                builder.release_name(version)

    def test_smoke_and_render_status_are_mandatory(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / 'result.json'
            for result in ({'status': 'failed', 'failed': []}, {'status': 'passed', 'failed': ['oops']}, {}):
                path.write_text(json.dumps(result))
                with self.assertRaises(ValueError):
                    builder.require_passed(path)
            path.write_text('{"status":"passed","failed":[]}')
            self.assertEqual('passed', builder.require_passed(path)['status'])

    def test_dirty_source_cannot_claim_release_provenance(self):
        with patch.object(builder.subprocess, 'check_output', side_effect=['a' * 40, ' M source.cs']):
            with self.assertRaisesRegex(ValueError, 'clean committed'):
                builder.source_snapshot()
        with patch.object(builder.subprocess, 'check_output', side_effect=['a' * 40, '']):
            self.assertEqual('a' * 40, builder.source_snapshot())

    def test_release_document_links_cannot_copy_private_files(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / 'repo'; output = Path(temp) / 'player'
            (root / 'ExampleProject').mkdir(parents=True); output.mkdir()
            secret = Path(temp) / 'unity_license.json'; secret.write_text('SECRET')
            try:
                (root / 'ExampleProject/README.md').symlink_to(secret)
            except OSError:
                self.skipTest('Symlink creation unavailable')
            with patch.object(builder, 'ROOT', root):
                with self.assertRaisesRegex(ValueError, 'Linked notice input'):
                    builder.copy_release_documents(output)
            self.assertEqual([], list(output.iterdir()))


if __name__ == '__main__':
    unittest.main()
