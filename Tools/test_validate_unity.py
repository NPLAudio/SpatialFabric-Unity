"""Offline isolation regression: no working caches or user persistent-data identity."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('validate_unity', Path(__file__).with_name('validate_unity.py'))
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)


class UnityIsolationTests(unittest.TestCase):
    def test_recursive_source_or_package_copy_rejected_before_writes(self):
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as folder:
            source, package = Path(folder)/'Source', Path(folder)/'Package'
            source.mkdir()
            package.mkdir()
            for root in (source, package):
                target = root/'Assets/Evidence/Project'
                with self.subTest(root=root), self.assertRaises(RuntimeError):
                    validator.prepare_isolated_project(source, package, target)
                self.assertFalse((root/'Assets').exists())
            with patch('sys.argv', ['validate_unity.py', '--unity', 'unused', '--isolated',
                                   '--project', str(source), '--output', str(source/'Assets/Evidence')]):
                with self.assertRaises(RuntimeError):
                    validator.main()
            self.assertFalse((source/'Assets').exists())

    def test_current_package_and_unique_silent_identity_only_in_copy(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source = root/'Source'
            package = root/'Package'
            for name in ('Assets', 'ProjectSettings', 'Packages', 'Library', 'Temp'):
                (source/name).mkdir(parents=True)
            package.mkdir()
            (package/'current.txt').write_text('current source')
            original_settings = 'PlayerSettings:\n  companyName: DefaultCompany\n  productName: ExampleProject\n'
            (source/'ProjectSettings/ProjectSettings.asset').write_text(original_settings)
            (source/'ProjectSettings/AudioManager.asset').write_text('AudioManager:\n  m_DisableAudio: 0\n')
            original_manifest = {'dependencies': {'com.nplaudio.spatialfabric': 'file:../../Packages/com.nplaudio.spatialfabric'}, 'testables': ['com.nplaudio.spatialfabric']}
            (source/'Packages/manifest.json').write_text(json.dumps(original_manifest))
            (source/'Packages/packages-lock.json').write_text('{}')
            target = root/'Isolated'
            validator.prepare_isolated_project(source, package, target)
            self.assertFalse((target/'Library').exists())
            self.assertFalse((target/'Temp').exists())
            self.assertFalse((target/'Packages/packages-lock.json').exists())
            self.assertEqual((target/'Packages/com.nplaudio.spatialfabric/current.txt').read_text(), 'current source')
            manifest = json.loads((target/'Packages/manifest.json').read_text())
            self.assertEqual(manifest['dependencies']['com.nplaudio.spatialfabric'], 'file:com.nplaudio.spatialfabric')
            self.assertIn('companyName: NPLAudioValidation', (target/'ProjectSettings/ProjectSettings.asset').read_text())
            self.assertIn('productName: SpatialFabricTests_', (target/'ProjectSettings/ProjectSettings.asset').read_text())
            self.assertIn('m_DisableAudio: 1', (target/'ProjectSettings/AudioManager.asset').read_text())
            self.assertEqual((source/'ProjectSettings/ProjectSettings.asset').read_text(), original_settings)
            self.assertEqual(json.loads((source/'Packages/manifest.json').read_text()), original_manifest)
            self.assertIn('m_DisableAudio: 0', (source/'ProjectSettings/AudioManager.asset').read_text())


class UnityEvidenceTests(unittest.TestCase):
    def evidence(self):
        root = validator.ET.Element('test-run', result='Skipped:Ignored', total='2', passed='1', failed='0', inconclusive='0', skipped='1')
        validator.ET.SubElement(root, 'test-case', result='Passed', fullname='PassedTest')
        name, reason = next(iter(validator.GRAPHICS_SKIPS.items()))
        case = validator.ET.SubElement(root, 'test-case', result='Skipped', fullname=name)
        validator.ET.SubElement(validator.ET.SubElement(case, 'reason'), 'message').text = reason
        return root

    def test_known_headless_skips_do_not_hide_passing_cases(self):
        self.assertEqual(validator.check_results(self.evidence())['passed'], '1')
        with self.assertRaises(RuntimeError):
            validator.check_results(self.evidence(), graphics=True)

    def test_unknown_skip_is_not_accepted(self):
        root = self.evidence()
        list(root)[1].set('fullname', 'UnexpectedSkippedTest')
        with self.assertRaises(RuntimeError):
            validator.check_results(root)

    def test_aggregate_success_never_substitutes_for_case_evidence(self):
        for mutate in (lambda root: root.set('passed', '2'),
                       lambda root: list(root)[0].set('result', 'Failed'),
                       lambda root: root.remove(list(root)[0]),
                       lambda root: root.set('result', 'Failed')):
            root = self.evidence()
            mutate(root)
            with self.assertRaises(RuntimeError):
                validator.check_results(root)


if __name__ == '__main__':
    unittest.main()
