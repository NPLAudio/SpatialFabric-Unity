// Explicit release player export (non-development). Never plays audio or starts networking during the build. MIT.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
namespace NPLAudio.SpatialFabric.Example.Editor
{
    public static class BuildReleasePlayer
    {
        public const string ExecutableName = "SpatialFabricRoomShowcase.exe";
        public static void Export()
        {
            string[] args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, "-spatialFabricBuildDirectory");
            if (i < 0 || i + 1 >= args.Length) throw new ArgumentException("A new empty build directory is required");
            string path = Path.GetFullPath(args[i + 1]);
            if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length > 0) throw new InvalidOperationException("Refusing to build into a non-empty directory: " + path);
            Directory.CreateDirectory(path);
            if (!Audio.NativeAudio.IsAvailable) throw new InvalidOperationException("Build/install the native DLL first: " + Audio.NativeAudio.AvailabilityError);
            if (!File.Exists(ShowcaseRooms.RoomScenePath)) throw new FileNotFoundException("Room scene missing", ShowcaseRooms.RoomScenePath);
            var options = new BuildPlayerOptions { scenes = new[] { ShowcaseRooms.RoomScenePath }, locationPathName = Path.Combine(path, ExecutableName), target = BuildTarget.StandaloneWindows64, options = BuildOptions.StrictMode };
            // Mono is the validated backend here; IL2CPP needs the editor module (see VALIDATION.md).
            var old = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            try
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
                var report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0) throw new InvalidOperationException("Release player build failed: " + report.summary.result);
            }
            finally { PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, old); }
        }
    }
}
