import hashlib,json,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
EXAMPLE=ROOT/'ExampleProject'
class RoomShowcaseTests(unittest.TestCase):
    def test_original_source_hashes(self):
        base=EXAMPLE/'SourceAssets';manifest=json.loads((base/'PROVENANCE.json').read_text())
        for name,sha in manifest['sha256'].items():
            path=base/name if name.endswith('.json') else base/'Audio'/name
            self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(),sha,name)
    def test_four_room_geometry_plan(self):
        data=json.loads((EXAMPLE/'SourceAssets/RoomLayout.json').read_text());self.assertEqual(len(data['rooms']),4);self.assertEqual(len(data['doors']),4);self.assertEqual(len(data['walls']),7);self.assertEqual(len(data['floorSamples']),10)
        self.assertTrue(all(room['doorWidth']>=200 for room in data['rooms']));self.assertTrue(any(box['name']=='Gallery foundation' for box in data['boxes']))
    def test_saved_room_scene_and_sounds_exist(self):
        folder=EXAMPLE/'Assets/SpatialFabricShowcase';scene=folder/'Scenes/RoomShowcase.unity';self.assertTrue(scene.is_file())
        for name in ['MonoCue','StereoLoop','ReplayCue']:self.assertTrue((folder/f'GeneratedRooms/SW_SF_{name}.asset').is_file())
        text=scene.read_text();self.assertIn('enableInEditor: 0',text);self.assertIn('enableInPlay: 0',text)
        for cue in ['mono','stereo','replay']:self.assertIn('/unity/playback/'+cue,text)
    def test_signs_use_depth_testing_not_overlay(self):
        shader=(EXAMPLE/'Assets/SpatialFabricShowcase/RoomText.shader').read_text();self.assertIn('ZTest LEqual',shader);self.assertNotIn('ZTest Always',shader)
        builder=(EXAMPLE/'Assets/SpatialFabricShowcase/Editor/BuildRoomShowcase.cs').read_text();self.assertIn('label.characterSize=size*.1f',builder)
    def test_walking_and_controls_are_real(self):
        visitor=(EXAMPLE/'Assets/SpatialFabricShowcase/ShowcaseVisitor.cs').read_text();self.assertIn('CharacterController',visitor);self.assertIn('Body.Move',visitor);self.assertIn('cue.TryPlay()',visitor)
        hud=(EXAMPLE/'Assets/SpatialFabricShowcase/ShowcaseRoomHud.cs').read_text();self.assertIn('StartNetworking()',hud);self.assertIn('cue.TryPlay()',hud);self.assertIn('StopAll()',hud)
if __name__=='__main__':unittest.main()
