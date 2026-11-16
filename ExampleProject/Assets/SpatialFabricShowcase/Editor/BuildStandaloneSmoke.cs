// Explicit disposable player export; never runs audio during the editor build. MIT.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Build;
namespace NPLAudio.SpatialFabric.Example.Editor
{
    public static class BuildStandaloneSmoke
    {
        public static void Export()
        {
            string[] args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-spatialFabricBuildDirectory");
            if(i<0 || i+1>=args.Length)throw new ArgumentException("Disposable build directory required");
            string path=Path.GetFullPath(args[i+1]);Directory.CreateDirectory(path);
            if(!Audio.NativeAudio.IsAvailable)throw new InvalidOperationException("Build/install native DLL before player validation");
            var options=new BuildPlayerOptions {scenes=new[]{File.Exists(ShowcaseRooms.RoomScenePath)?ShowcaseRooms.RoomScenePath:BuildShowcase.ScenePath},locationPathName=Path.Combine(path,"SpatialFabricSmoke.exe"),target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development|BuildOptions.StrictMode};
            bool il2cpp=Array.IndexOf(args,"-spatialFabricIl2Cpp")>=0;var old=PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            try
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,il2cpp?ScriptingImplementation.IL2CPP:ScriptingImplementation.Mono2x);
                var report=BuildPipeline.BuildPlayer(options);
                if(report.summary.result!=BuildResult.Succeeded || report.summary.totalErrors!=0)throw new InvalidOperationException("Standalone build failed: "+report.summary.result);
            }
            finally{PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,old);}
        }
    }
}
