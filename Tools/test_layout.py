import hashlib,json,re,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
PACKAGE=ROOT/'Packages/com.nplaudio.spatialfabric'
class LayoutTests(unittest.TestCase):
    def test_related_repository_links(self):
        text=(ROOT/'README.md').read_text()
        for name in ('SpatialFabric','SpatialFabric-Unity','SpatialFabric-Interop','AudioLoom'):
            self.assertIn('https://github.com/NPLAudio/'+name,text)
    def test_upm_identity(self):
        p=json.loads((PACKAGE/'package.json').read_text());self.assertEqual(p['name'],'com.nplaudio.spatialfabric');self.assertRegex(p['version'],r'^0\.\d+\.\d+$');self.assertEqual(set(p['dependencies']),{'com.unity.modules.audio','com.unity.modules.physics','com.unity.modules.imgui','com.unity.modules.jsonserialize'})
    def test_runtime_never_imports_editor(self):
        for p in (PACKAGE/'Runtime').rglob('*.cs'):self.assertNotIn('UnityEditor',p.read_text(),str(p))
        self.assertNotIn('Editor',json.loads((PACKAGE/'Runtime/SpatialFabric.Runtime.asmdef').read_text())['references'])
    def test_assembly_boundaries(self):
        e=json.loads((PACKAGE/'Editor/SpatialFabric.Editor.asmdef').read_text());self.assertEqual(e['includePlatforms'],['Editor']);self.assertIn('SpatialFabric.Runtime',e['references'])
        for mode in ('EditMode','PlayMode'):
            d=json.loads((PACKAGE/f'Tests/{mode}/SpatialFabric.{mode}Tests.asmdef').read_text());self.assertIn('TestAssemblies',d['optionalUnityReferences'])
    def test_vendored_contract_hashes(self):
        folder=PACKAGE/'Tests/EditMode/Fixtures';data=json.loads((folder/'PROVENANCE.json').read_text())
        for name,sha in data['sha256'].items():self.assertEqual(hashlib.sha256((folder/Path(name).name).read_bytes()).hexdigest(),sha)
    def test_example_uses_local_package(self):
        manifest=ROOT/'ExampleProject/Packages/manifest.json';d=json.loads(manifest.read_text());ref=d['dependencies']['com.nplaudio.spatialfabric'];self.assertTrue(ref.startswith('file:'));self.assertEqual((manifest.parent/ref[5:]).resolve(),PACKAGE.resolve());self.assertIn('com.nplaudio.spatialfabric',d['testables'])
    def test_no_false_playback_implementation(self):
        text=(PACKAGE/'Runtime/Playback/PlaybackCue.cs').read_text();self.assertIn('BackendAvailable => NativeAudio.IsAvailable',text);self.assertIn('IsPlaying => observation.IsPlaying',text);self.assertNotIn('AddComponent<AudioSource>',text)
        self.assertIn('NativeAudio.CreatePlayer()',text);self.assertIn('No prepared PCM asset',text)
    def test_bound_endpoints_are_not_configured_endpoints(self):
        text=(PACKAGE/'Runtime/Discovery/SceneInventory.cs').read_text();self.assertIn('bound_ip = owner.BoundPlaybackIp',text);self.assertIn('bound_port = owner.BoundPlaybackPort',text)
    def test_runtime_never_mutates_queries_or_prepares_audio(self):
        text=(PACKAGE/'Runtime/Discovery/SceneInventory.cs').read_text();self.assertNotRegex(text,r'\b(?:Resources\.Load|AssetDatabase\.LoadAsset|\.LoadAudioData|\.GetData)\s*\(');self.assertNotIn('TryPlay(',text);self.assertNotIn('StartNetworking(',text)
    def test_ignore_generated_and_private_state(self):
        ignore=(ROOT/'.gitignore').read_text();self.assertIn('[Ll]ibrary/',ignore);self.assertIn('[Bb]in/',ignore);self.assertIn('.pi/',ignore)
    def test_docs_and_license_present(self):
        for p in ['README.md','LICENSE.md','Docs/PARITY.md','Native/README.md']:self.assertTrue((ROOT/p).is_file(),p)
        self.assertIn('External validation gates',(ROOT/'Docs/PARITY.md').read_text())
if __name__=='__main__':unittest.main()
