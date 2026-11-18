"""Offline assertions for actual-player evidence; no Unity/device required."""
import unittest
import tempfile
from pathlib import Path
from validate_player import check_report, check_output_path


class PlayerEvidenceTests(unittest.TestCase):
    def report(self):
        return {'status': 'passed', 'error': '', 'native_available': True,
                'offline_render': True, 'invalid_device_failed': True,
                'no_playing': True, 'scene_records': 7}

    def test_complete_player_report(self):
        self.assertEqual(check_report(self.report())['scene_records'], 7)
        report = self.report()
        report['scene_records'] = 9
        check_report(report)

    def test_status_alone_is_not_a_pass(self):
        with self.assertRaises(RuntimeError):
            check_report({'status': 'passed'})
        for key in ('native_available', 'offline_render', 'invalid_device_failed', 'no_playing'):
            report = self.report()
            report[key] = False
            with self.subTest(key=key), self.assertRaises(RuntimeError):
                check_report(report)

    def test_custom_project_cannot_contain_isolated_evidence(self):
        with tempfile.TemporaryDirectory() as tmp:
            source = Path(tmp)/'SourceProject'
            source.mkdir()
            for output in (source, source/'Assets/Evidence'):
                with self.subTest(output=output), self.assertRaises(RuntimeError):
                    check_output_path(output, source)
            self.assertFalse((source/'Assets').exists())
            check_output_path(Path(tmp)/'ExternalEvidence', source)

    def test_error_and_unexpected_scene_fail(self):
        for key, value in (('error', 'decode failed'), ('scene_records', 0), ('scene_records', '7')):
            report = self.report()
            report[key] = value
            with self.subTest(key=key, value=value), self.assertRaises(RuntimeError):
                check_report(report)


if __name__ == '__main__':
    unittest.main()
