// CLI-only actual-player geometry/PCM/animation smoke, with optional offscreen room renders. MIT.
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using NPLAudio.SpatialFabric.Audio;
namespace NPLAudio.SpatialFabric.Example
{
    public sealed class RoomShowcaseSmoke : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            if(Application.isEditor)return;string[] args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-spatialFabricRoomsSmoke");if(i<0 || i+1>=args.Length)return;
            int shots=Array.IndexOf(args,"-spatialFabricRoomShots");string imageFolder=shots>=0 && shots+1<args.Length?args[shots+1]:null;
            new GameObject("Silent room validation").AddComponent<RoomShowcaseSmoke>().StartCoroutine(Run(args[i+1],imageFolder));
        }
        static void Render(Camera camera,string path,Vector3 position,Quaternion rotation)
        {
            Vector3 oldPosition=camera.transform.position;Quaternion oldRotation=camera.transform.rotation;
            var target=new RenderTexture(1280,720,24);var image=new Texture2D(1280,720,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try{camera.transform.SetPositionAndRotation(position,rotation);camera.targetTexture=target;camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=previous;target.Release();Destroy(target);Destroy(image);camera.transform.SetPositionAndRotation(oldPosition,oldRotation);}
        }
        static IEnumerator Run(string reportPath,string imageFolder)
        {
            yield return null;var rooms=UnityObjects.FindAll<ShowcaseRooms>()[0];rooms.visitor.automationMode=true;
            var report=RoomValidation.Validate(rooms,true);Vector3 stage=rooms.manager.stage.transform.position;
            Vector3[] before=new Vector3[4];for(int i=0;i<4;++i)before[i]=rooms.trackedObjects[i].transform.position;
            double until=Time.realtimeSinceStartupAsDouble+.5;while(Time.realtimeSinceStartupAsDouble<until)yield return null;
            int changed=0;for(int i=0;i<4;++i)if(Vector3.Distance(before[i],rooms.trackedObjects[i].transform.position)>.01f)++changed;
            report.Check(changed==3,"three objects animate in real player");report.Check(rooms.manager.stage.transform.position==stage,"stage stays fixed while visiting hallway");
            foreach(var cue in rooms.cues)
            {
                bool valid=false;string error="";
                try
                {
                    using(var buffer=NativeAudio.Prepare(cue.preparedAudio.Snapshot())){double p=0;var pcm=NativeAudio.RenderOffline(buffer,256,cue.preparedAudio.Channels,48000,0,false,ref p,out _);valid=pcm.Length==256*cue.preparedAudio.Channels;foreach(float sample in pcm)if(float.IsNaN(sample)||float.IsInfinity(sample))valid=false;}
                }catch(Exception e){error=e.Message;}
                report.Check(valid,"offline native PCM / "+cue.oscBase+error);
            }
            report.Check(Array.TrueForAll(rooms.cues,c=>!c.IsPlaying && !c.IsPrepared && !c.ReplayPending),"offline audit did not start or prepare any cue player");
            if(!string.IsNullOrEmpty(imageFolder))
            {
                if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)report.Check(false,"room screenshots require graphics");
                else
                {
                    Directory.CreateDirectory(imageFolder);
                    Render(rooms.visitor.viewCamera,Path.Combine(imageFolder,"hall.png"),new Vector3(-6,1.73f,-14.8f),Quaternion.Euler(0,55,0));
                    Render(rooms.visitor.viewCamera,Path.Combine(imageFolder,"spatial.png"),new Vector3(-.8f,1.73f,-10.5f),Quaternion.Euler(-5,8,0));
                    for(int i=1;i<4;++i)Render(rooms.visitor.viewCamera,Path.Combine(imageFolder,new[]{"","mono.png","stereo.png","replay.png"}[i]),new Vector3(rooms.plan.rooms[i].centerRight*.01f,1.73f,-10),Quaternion.identity);
                    report.Check(true,"five real room renders saved");
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));File.WriteAllText(reportPath,JsonUtility.ToJson(report,true));Application.Quit(report.failed.Count==0?0:1);
        }
    }
}
