// Actual runtime controls, not decorative in-world buttons. No automatic playback/listening. MIT.
using UnityEngine;
using NPLAudio.SpatialFabric.Audio;
namespace NPLAudio.SpatialFabric.Example
{
    public sealed class ShowcaseRoomHud : MonoBehaviour
    {
        public ShowcaseRooms rooms;
        public bool visible=true;
        bool settings;
        Vector2 scroll;
        string message="";
        void OnGUI()
        {
            if(!visible || rooms==null)return;
            GUILayout.BeginArea(new Rect(15,15,Mathf.Min(560,Screen.width-30),Mathf.Min(settings?650:285,Screen.height-30)),GUI.skin.box);
            scroll=GUILayout.BeginScrollView(scroll);
            int index=rooms.RoomAt(rooms.visitor.transform.position);
            GUILayout.Label("SPATIALFABRIC  /  "+(index<0?"HALLWAY":rooms.plan.rooms[index].name.ToUpperInvariant()));
            GUILayout.Label("WASD + mouse | Shift sprint | Space jump | Esc cursor");
            GUILayout.Label("E play / Q stop room cue | L loop | R replay | X stop all");
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Walk / capture mouse"))rooms.visitor.CaptureMouse();
            if(GUILayout.Button("Stop ALL audio"))rooms.StopAll();
            settings=GUILayout.Toggle(settings,"Routing / OSC","Button");GUILayout.EndHorizontal();
            var cue=rooms.CurrentCue;
            if(cue!=null)
            {
                GUILayout.Label($"{cue.oscBase}  |  {cue.WorkerState}  |  output {cue.outputChannel}");
                GUILayout.BeginHorizontal();if(GUILayout.Button("Play this cue"))cue.TryPlay();if(GUILayout.Button("Stop + cancel replay"))cue.Stop();
                bool loop=GUILayout.Toggle(cue.loop,"Loop");if(loop!=cue.loop)cue.SetLoop(loop);cue.autoReplay=GUILayout.Toggle(cue.autoReplay,"Auto replay");GUILayout.EndHorizontal();
                if(!string.IsNullOrEmpty(cue.LastError))GUILayout.Label(cue.LastError);
            }
            else GUILayout.Label(index==0?"Spatial data only. Start OSC explicitly below; track ADM 50019 / Custom 9102.":"Enter a playback room for its cue controls.");
            GUILayout.Label("Direct PCM is not spatialized by these walls or OSC positions. No audio auto-starts.");
            if(settings)
            {
                GUILayout.Space(8);GUILayout.Label("OSC inputs 127.0.0.1:8200 / 9100; state 9101");
                GUILayout.BeginHorizontal();
                if(GUILayout.Button(rooms.manager.IsListening?"Stop local OSC":"Start OSC (explicit)"))
                {
                    if(rooms.manager.IsListening)rooms.manager.StopNetworking();
                    else{rooms.manager.enableInPlay=true;rooms.manager.StartNetworking();}
                }
                if(GUILayout.Button("Refresh output devices")){NativeAudio.Probe();NativeAudio.RefreshDevices();message=NativeAudio.DeviceEnumerationError;}
                GUILayout.EndHorizontal();
                GUILayout.Label(rooms.manager.IsListening?"OSC listening; position output enabled":"OSC stopped (stopping OSC does not stop PCM)");
                bool busy=false;foreach(var c in rooms.cues)if(c.IsPlaying || c.ReplayPending || c.WorkerState==AudioWorkerState.Starting)busy=true;
                GUI.enabled=!busy;
                if(GUILayout.Button("Use system default output for all cues"))foreach(var c in rooms.cues)c.configuredDeviceId="";
                foreach(var device in NativeAudio.CachedDevices)
                    if(GUILayout.Button($"{device.name} — {device.channels} ch / {device.sample_rate} Hz"))foreach(var c in rooms.cues)c.configuredDeviceId=device.id;
                foreach(var c in rooms.cues)
                {
                    GUILayout.BeginHorizontal();GUILayout.Label(c.oscBase+"  channel",GUILayout.Width(335));
                    string channel=GUILayout.TextField(c.outputChannel.ToString(),3,GUILayout.Width(55));
                    if(int.TryParse(channel,out int n) && n>=0 && n<=64)c.outputChannel=n;
                    GUILayout.EndHorizontal();
                }
                GUI.enabled=true;
                GUILayout.Label("Channel 0 = matching source/device channels; 1..N = first source channel to one output. Stop all audio/replay before editing routing.");
                if(!NativeAudio.IsAvailable)GUILayout.Label(NativeAudio.AvailabilityError);
                if(!string.IsNullOrEmpty(message))GUILayout.Label(message);
            }
            GUILayout.EndScrollView();GUILayout.EndArea();
        }
    }
}
