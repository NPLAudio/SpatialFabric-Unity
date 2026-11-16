// Original SpatialFabric example; MIT.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace NPLAudio.SpatialFabric.Example.Editor
{
    public static class BuildShowcase
    {
        public const string ScenePath = "Assets/SpatialFabricShowcase/Scenes/SpatialExample.unity";
        [MenuItem("Tools/SpatialFabric/Build spatial example (new scene)")]
        public static void Interactive()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("Replace generated example?",ScenePath,"Replace","Cancel")) return;
            Build();
        }
        public static void BuildBatch()
        {
            if (File.Exists(ScenePath)) throw new System.InvalidOperationException("Refusing to overwrite an existing example in batch mode");
            Build();
        }
        public static SpatialFabricManager Build()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root = new GameObject("SpatialFabric Unity Example");
            var stage = new GameObject("Stage — fixed basis").AddComponent<SpatialStage>(); stage.transform.SetParent(root.transform);
            stage.dimensionsMeters = new Vector3(10,10,6);
            var manager = new GameObject("SpatialFabric Manager").AddComponent<SpatialFabricManager>(); manager.transform.SetParent(root.transform); manager.stage = stage;
            // Side-by-side ports do not collide with an already-running Unreal showcase.
            manager.spatialInputPort = 8200; manager.playbackInputPort = 9100; manager.admPort = 50019; manager.customPort = 9102; manager.monitorPort = 9101;
            Vector3[] positions = {new Vector3(0,0,3),new Vector3(3,0,0),new Vector3(0,2,0),new Vector3(-2,0,-2)};
            string[] names = {"Front","Right","Up","Moving object"};
            for (int i = 0; i < positions.Length; ++i)
            {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere); obj.name = names[i]; obj.transform.SetParent(root.transform); obj.transform.position = positions[i]; obj.transform.localScale = Vector3.one*.5f;
                Object.DestroyImmediate(obj.GetComponent<Collider>());
                var spatial = obj.AddComponent<SpatialObject>(); spatial.objectId = i+1; manager.objects.Add(spatial);
                if (i == 3) obj.AddComponent<ShowcaseMotion>();
            }
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Floor"; floor.transform.SetParent(root.transform);
            floor.transform.position = new Vector3(0,-3.1f,0); floor.transform.localScale = new Vector3(10,.2f,10);
            var camera = new GameObject("Camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform); camera.transform.position = new Vector3(9,8,-12); camera.transform.LookAt(Vector3.zero); camera.tag = "MainCamera";
            var light = new GameObject("Directional light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.SetParent(root.transform); light.transform.rotation = Quaternion.Euler(50,30,0);
            var cue = new GameObject("Playback cue — assign prepared PCM (no autoplay)").AddComponent<PlaybackCue>(); cue.transform.SetParent(root.transform); cue.oscBase = "/unity/playback/mono";
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)); AssetDatabase.Refresh();
            EditorSceneManager.SaveScene(manager.gameObject.scene,ScenePath);
            EditorBuildSettings.scenes = new[] {new EditorBuildSettingsScene(ScenePath,true)};
            Selection.activeGameObject = manager.gameObject;
            Debug.Log("SpatialFabric spatial example created. Networking is stopped; no audio started.");
            return manager;
        }
    }
}
