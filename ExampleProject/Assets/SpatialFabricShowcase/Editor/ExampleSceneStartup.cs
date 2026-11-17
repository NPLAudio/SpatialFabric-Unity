using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NPLAudio.SpatialFabric.Example.Editor
{
    // Batch test runners can leave Unity's last-scene list empty. Restore the saved
    // example only on a clean startup scene, never over user work or during tests.
    [InitializeOnLoad]
    public static class ExampleSceneStartup
    {
        const string CheckedKey = "SpatialFabric.Example.StartupSceneChecked";

        static ExampleSceneStartup()
        {
            if (!Application.isBatchMode && !SessionState.GetBool(CheckedKey, false))
                EditorApplication.update += OpenWhenReady;
        }

        static void OpenWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= OpenWhenReady;
            SessionState.SetBool(CheckedKey, true);
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.sceneCount != 1) return;
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.isDirty || !string.IsNullOrEmpty(scene.path)) return;
            var roots = scene.GetRootGameObjects();
            bool empty = roots.Length == 0 || (roots.Length == 2 &&
                roots.Any(r => r.name == "Main Camera" && r.GetComponent<Camera>() != null && r.transform.childCount == 0) &&
                roots.Any(r => r.name == "Directional Light" && r.GetComponent<Light>() != null && r.transform.childCount == 0));
            if (!empty || !File.Exists(ShowcaseRooms.RoomScenePath)) return;
            BuildRoomShowcase.Open();
            Debug.Log("SpatialFabric: opened saved four-room showcase from an empty startup scene. Audio and OSC remain stopped.");
        }
    }
}
