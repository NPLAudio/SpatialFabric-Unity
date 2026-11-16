// Explicit CLI-only SILENT smoke. Not activated during normal example use. MIT.
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using NPLAudio.SpatialFabric.Audio;
namespace NPLAudio.SpatialFabric.Example
{
    public sealed class StandaloneAudioSmoke : MonoBehaviour
    {
        [Serializable] sealed class Report { public string status,error; public bool native_available,offline_render,invalid_device_failed,no_playing; public int scene_records; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            if(Application.isEditor)return;string[] args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-spatialFabricSmoke");
            if(i<0 || i+1>=args.Length)return;
            new GameObject("Silent standalone validation").AddComponent<StandaloneAudioSmoke>().StartCoroutine(Run(args[i+1]));
        }
        static IEnumerator Run(string path)
        {
            var report=new Report();NativeAudio.Buffer buffer=null;NativeAudio.Player player=null;
            try
            {
                NativeAudio.Probe();report.native_available=NativeAudio.IsAvailable;
                if(!report.native_available)throw new InvalidOperationException(NativeAudio.AvailabilityError);
                buffer=NativeAudio.Prepare(new PcmData(new[]{.25f,-.5f,.75f,-1},2,48000));double p=0;
                var samples=NativeAudio.RenderOffline(buffer,3,2,48000,0,false,ref p,out bool ended);
                report.offline_render=ended && samples.Length==6 && samples[0]==.25f && samples[1]==-.5f && samples[4]==0;
                if(!report.offline_render)throw new InvalidOperationException("Offline PCM routing/EOF failed");
                player=NativeAudio.CreatePlayer();if(!player.Start(buffer,"spatialfabric-smoke-invalid-device-no-fallback",0,false,0,false))throw new InvalidOperationException("Invalid-device request not accepted for test");
            }
            catch(Exception e) {report.error=e.Message;}
            if(string.IsNullOrEmpty(report.error))
            {
                double deadline=Time.realtimeSinceStartupAsDouble+5;AudioStatus status=player.Poll();
                while(status.State==AudioWorkerState.Starting && Time.realtimeSinceStartupAsDouble<deadline) {yield return null;status=player.Poll();}
                report.invalid_device_failed=status.State==AudioWorkerState.Failed && status.StartedSequence==0 && status.EndedSequence==0;
                report.no_playing=true;
                foreach(var cue in UnityObjects.FindAll<PlaybackCue>())if(cue.IsPlaying)report.no_playing=false;
                var managers=UnityObjects.FindAll<SpatialFabricManager>();
                if(managers.Length==1)report.scene_records=JsonUtility.FromJson<ScenePage>(SceneInventory.GetJson(managers[0])).total;
            }
            player?.Dispose();buffer?.Dispose();
            report.status=report.native_available && report.offline_render && report.invalid_device_failed && report.no_playing && report.scene_records==(UnityObjects.FindAll<ShowcaseRooms>().Length==1?9:7) && string.IsNullOrEmpty(report.error)?"passed":"failed";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));File.WriteAllText(path,JsonUtility.ToJson(report,true));
            Application.Quit(report.status=="passed"?0:1);
        }
    }
}
