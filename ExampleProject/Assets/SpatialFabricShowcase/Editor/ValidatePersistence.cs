// Separate batch-only scene lifecycle gate: never closes the NUnit runner/user editor. MIT.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using NPLAudio.SpatialFabric.Audio;
namespace NPLAudio.SpatialFabric.Example.Editor
{
    public static class ValidatePersistence
    {
        [Serializable] sealed class Report{public string status;public List<string> passed=new List<string>();}
        public static void Run()
        {
            if(!Application.isBatchMode)throw new InvalidOperationException("Persistence validation runs only in its own batch editor");
            string[] args=Environment.GetCommandLineArgs();int p=Array.IndexOf(args,"-spatialFabricEvidence");if(p<0 || p+1>=args.Length)throw new ArgumentException("Evidence required");
            var setup=EditorSceneManager.GetSceneManagerSetup();string folder="Assets/__SpatialFabricPersistence_"+Guid.NewGuid().ToString("N");AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));var report=new Report();
            void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);report.passed.Add(label);}
            try
            {
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var binding=new GameObject("Binding").AddComponent<SpatialObject>();var pose=new GameObject("Explicit target");binding.boundTransform=pose.transform;binding.objectId=51;
                var manager=new GameObject("Manager").AddComponent<SpatialFabricManager>();manager.stage=new GameObject("Stage").AddComponent<SpatialStage>();manager.objects.Add(binding);
                var asset=ScriptableObject.CreateInstance<PreparedAudioAsset>();asset.SetPcm(new PcmData(new[]{.25f},1,48000),"persistence-only");AssetDatabase.CreateAsset(asset,folder+"/PCM.asset");var cue=new GameObject("Cue").AddComponent<PlaybackCue>();cue.preparedAudio=asset;
                string path=folder+"/Scene.unity";Check(EditorSceneManager.SaveScene(scene,path),"save original scene");string key=binding.Key;
                if(NativeAudio.IsAvailable){Check(cue.Prepare(),"explicit offline PCM cache preparation");var handle=(NativeAudio.Buffer)typeof(PlaybackCue).GetField("buffer",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(cue);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Check(handle.IsClosed,"scene unload releases native PCM handle");}
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var loaded=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);binding=SceneInventory.InScene<SpatialObject>(loaded)[0];manager=SceneInventory.InScene<SpatialFabricManager>(loaded)[0];cue=SceneInventory.InScene<PlaybackCue>(loaded)[0];
                Check(binding.Key==key,"persistent scene identity after reload");Check(binding.objectId==51 && binding.Pose.name=="Explicit target","pose reference and ID after reload");Check(cue.preparedAudio!=null && !cue.IsPrepared && !cue.IsPlaying,"source reference restored without preparation/play");Check(!manager.IsListening,"reload does not start networking");
                var second=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);var foreign=new GameObject("Foreign binding").AddComponent<SpatialObject>();SceneManager.MoveGameObjectToScene(foreign.gameObject,second);manager.objects.Add(foreign);
                Check(!manager.Resolves(foreign),"additive scene references excluded from owner world");string json=SceneInventory.GetJson(manager);Check(!json.Contains("Foreign binding"),"query does not leak another scene's binding");
                binding.boundTransform=foreign.transform;Check(!manager.Resolves(binding),"cross-scene pose cannot transmit");binding.boundTransform=null;
                Check(EditorSceneManager.SaveScene(second,folder+"/Additive.unity"),"save additive scene");
                manager.additionalScenePaths.Add(second.path);Check(manager.Resolves(foreign),"explicit additive world resolves its binding");Check(SceneInventory.GetJson(manager).Contains("Foreign binding"),"explicit additive world inventory includes opted-in scene");manager.additionalScenePaths.Clear();
                EditorSceneManager.CloseScene(second,true);Check(SceneInventory.InScene<SpatialObject>(loaded).Length==1,"additive unload keeps owner registry scoped");
                report.status="passed";
            }
            finally
            {
                if(Array.Exists(setup,s=>s.isLoaded && s.isActive))EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                AssetDatabase.DeleteAsset(folder);Directory.CreateDirectory(args[p+1]);File.WriteAllText(Path.Combine(args[p+1],"persistence.json"),JsonUtility.ToJson(report,true));
            }
        }
    }
}
