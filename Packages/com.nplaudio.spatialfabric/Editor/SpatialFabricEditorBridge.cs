// Copyright (c) 2026 SpatialFabric Contributors. MIT.
using UnityEditor;
using UnityEngine;
namespace NPLAudio.SpatialFabric.Editor
{
    [InitializeOnLoad]
    public static class SpatialFabricEditorBridge
    {
        static SpatialFabricEditorBridge()
        {
            SceneInventory.RegisterMainThread();
            Audio.NativeAudio.Probe();
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += StopAll;
            EditorApplication.quitting += StopAll;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode) StopAll(); };
        }
        static void Tick()
        {
            if (EditorApplication.isCompiling) return;
            bool playerPaused = EditorApplication.isPlaying && EditorApplication.isPaused;
            if (!EditorApplication.isPlaying || playerPaused)
                foreach (var cue in UnityObjects.FindAll<PlaybackCue>(false)) if (cue.isActiveAndEnabled) cue.Tick(Time.realtimeSinceStartupAsDouble);
            if (EditorApplication.isPlayingOrWillChangePlaymode && !playerPaused) return;
            foreach (var manager in UnityObjects.FindAll<SpatialFabricManager>(false))
                if (manager.isActiveAndEnabled) manager.Tick(Time.realtimeSinceStartupAsDouble);
        }
        static void StopAll()
        {
            foreach (var manager in UnityObjects.FindAll<SpatialFabricManager>()) manager.StopNetworking();
            foreach (var cue in UnityObjects.FindAll<PlaybackCue>()) cue.ReleaseAudio();
        }
    }
}
