// Explicit, silent batch probe. Does not save transient networking permissions into the example.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace NPLAudio.SpatialFabric.Example.Editor
{
    public static class LiveInteropProbe
    {
        [Serializable] sealed class Result
        {
            public string status,scope = "OSC/query only; no PCM preparation, device enumeration or playback requested";
            public bool configuration_unchanged,any_playing;
            public long sent,received;
        }
        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args,"-spatialFabricEvidence");
            if (index < 0 || index+1 >= args.Length) throw new ArgumentException("Evidence directory required");
            string folder = args[index+1]; Directory.CreateDirectory(folder);
            EditorSceneManager.OpenScene(Array.IndexOf(args,"-spatialFabricRoomScene")>=0?ShowcaseRooms.RoomScenePath:BuildShowcase.ScenePath);
            var manager = UnityObjects.FindAll<SpatialFabricManager>().Single();
            if (manager.spatialInputPort != 8200 || manager.playbackInputPort != 9100 || manager.admPort != 50019 || manager.customPort != 9102) throw new InvalidOperationException("Probe requires the generated side-by-side example ports");
            manager.enableInEditor = true;
            if (!manager.StartNetworking()) throw new InvalidOperationException(manager.LastError);
            string before = EditorJsonUtility.ToJson(manager);
            File.WriteAllText(Path.Combine(folder,"ready.json"),"{\"status\":\"ready\",\"spatial_port\":8200,\"playback_port\":9100}");
            double started = EditorApplication.timeSinceStartup;
            EditorApplication.CallbackFunction finish = null;
            finish = () =>
            {
                if (EditorApplication.timeSinceStartup-started < 20) return;
                EditorApplication.update -= finish;
                bool same = before == EditorJsonUtility.ToJson(manager);
                bool playing = SceneInventory.InScene<PlaybackCue>(manager.gameObject.scene).Any(cue => cue.IsPlaying);
                var result = new Result {status = same && !playing ? "passed" : "failed",configuration_unchanged = same,any_playing = playing,sent = manager.Sent,received = manager.Received};
                manager.StopNetworking();
                File.WriteAllText(Path.Combine(folder,"engine-result.json"),JsonUtility.ToJson(result,true));
                EditorApplication.Exit(result.status == "passed" ? 0 : 1);
            };
            EditorApplication.update += finish;
        }
    }
}
