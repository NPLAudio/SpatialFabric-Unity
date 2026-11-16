// Functional operator/authoring panels; explicit actions, no startup I/O. MIT.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using NPLAudio.SpatialFabric.Audio;
namespace NPLAudio.SpatialFabric.Editor
{
    public sealed class SpatialFabricWindow : EditorWindow
    {
        SpatialFabricManager manager;
        Vector2 scroll;
        RadarView radarView;
        int tab,firstId=1;
        readonly HashSet<SpatialObject> selected=new HashSet<SpatialObject>();
        [MenuItem("Tools/SpatialFabric/Open Window")]
        public static void Open(){var window=GetWindow<SpatialFabricWindow>("SpatialFabric");window.minSize=new Vector2(680,480);if(Selection.activeGameObject!=null)window.manager=Selection.activeGameObject.GetComponent<SpatialFabricManager>();}
        void Action(Action action){try{action();}catch(Exception e){EditorUtility.DisplayDialog("SpatialFabric action refused",e.Message,"OK");}}
        static void Fields(SerializedObject target,params string[] names){foreach(string name in names){var property=target.FindProperty(name);if(property!=null)EditorGUILayout.PropertyField(property,true);}}
        void OnGUI()
        {
            EditorGUILayout.LabelField("SpatialFabric — stages, routing and direct playback",EditorStyles.boldLabel);
            manager=(SpatialFabricManager)EditorGUILayout.ObjectField("Loaded-scene manager",manager,typeof(SpatialFabricManager),true);
            if(manager==null)
            {
                if(GUILayout.Button("Use selected manager") && Selection.activeGameObject!=null)manager=Selection.activeGameObject.GetComponent<SpatialFabricManager>();
                if(GUILayout.Button("Create stage + manager (Undo)"))Action(()=>manager=SpatialAuthoring.CreateManager());return;
            }
            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUI.DisabledScope(manager.IsListening))if(GUILayout.Button("Start configured OSC"))manager.StartNetworking();
                using(new EditorGUI.DisabledScope(!manager.IsListening))if(GUILayout.Button("Stop local OSC"))manager.StopNetworking();
                if(GUILayout.Button("Copy scene JSON"))EditorGUIUtility.systemCopyBuffer=SceneInventory.GetJson(manager);
            }
            EditorGUILayout.LabelField("Actual inputs",manager.IsListening?$"{manager.BoundSpatialIp}:{manager.BoundSpatialPort} / {manager.BoundPlaybackIp}:{manager.BoundPlaybackPort}":"Stopped");
            tab=GUILayout.Toolbar(tab,new[]{"Stage","Objects","Outputs","Playback","CSV","Diagnostics"});
            scroll=EditorGUILayout.BeginScrollView(scroll);
            switch(tab){case 0:Stage();break;case 1:Objects();break;case 2:Outputs();break;case 3:Playback();break;case 4:Csv();break;case 5:Diagnostics();break;}
            EditorGUILayout.EndScrollView();
            EditorGUILayout.HelpBox("OSC is not PCM. Stop OSC does not stop audio. Closing this window never starts/stops remote playback. Input changes require Stop/Start.",MessageType.None);
        }
        void Stage()
        {
            SpatialRadar.Draw(manager,ref radarView);
            var serialized=new SerializedObject(manager);serialized.Update();Fields(serialized,"stage","additionalScenePaths","enableInEditor","enableInPlay","runInBackground","listenIp","spatialInputPort","playbackInputPort","enableSceneQueries","allowRemoteSceneQueries");serialized.ApplyModifiedProperties();
            if(manager.stage!=null){EditorGUILayout.LabelField("Authored stage / virtual listener",EditorStyles.boldLabel);SpatialStageEditor.DrawStage(manager.stage);}
        }
        void Objects()
        {
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Bind selected scene objects"))Action(()=>SpatialAuthoring.AddBindings(manager,Selection.gameObjects));
                if(GUILayout.Button("Select all rows")){selected.Clear();foreach(var obj in manager.objects)if(obj!=null)selected.Add(obj);}
                if(GUILayout.Button("Clear row selection"))selected.Clear();
            }
            using(new EditorGUILayout.HorizontalScope())
            {
                firstId=EditorGUILayout.IntField("Sequential IDs from",firstId);
                if(GUILayout.Button("Assign to selected"))Action(()=>SpatialAuthoring.EditBindings(manager,selected,firstId));
                if(GUILayout.Button("Mute selected"))Action(()=>SpatialAuthoring.EditBindings(manager,selected,null,true));
                if(GUILayout.Button("Unmute"))Action(()=>SpatialAuthoring.EditBindings(manager,selected,null,false));
            }
            foreach(var obj in manager.objects.ToArray())
            {
                if(obj==null){EditorGUILayout.HelpBox("Missing binding reference; edit the binding list below.",MessageType.Warning);continue;}
                using(new EditorGUILayout.HorizontalScope())
                {
                    bool check=GUILayout.Toggle(selected.Contains(obj),"",GUILayout.Width(20));if(check)selected.Add(obj);else selected.Remove(obj);
                    EditorGUILayout.ObjectField(obj,typeof(SpatialObject),true);if(GUILayout.Button("Inspect",GUILayout.Width(70)))Selection.activeGameObject=obj.gameObject;
                }
                var serialized=new SerializedObject(obj);serialized.Update();Fields(serialized,"boundTransform","displayLabel","objectId","admObjectIdOverride","customObjectIdOverride","sendAdm","sendCustom","gain","width","mute","targetOverrides","customFields");serialized.ApplyModifiedProperties();
                if(manager.stage!=null)EditorGUILayout.LabelField("Current front/right/up",manager.stage.Normalize(obj.Pose.position).ToString("F3"));EditorGUILayout.Space(8);
            }
            var list=new SerializedObject(manager);list.Update();Fields(list,"objects");list.ApplyModifiedProperties();
        }
        void Outputs()
        {
            EditorGUILayout.HelpBox("A nonempty Outputs list replaces legacy endpoints. Every destination has its own identity, protocol, IP/port, rate and change cache. Object rows can override IDs/fields by Output Key.",MessageType.Info);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Add ADM destination"))AddOutput(Protocols.SpatialProtocol.ADM);
                if(GUILayout.Button("Add Custom destination"))AddOutput(Protocols.SpatialProtocol.Custom);
            }
            var s=new SerializedObject(manager);s.Update();Fields(s,"outputs");EditorGUILayout.LabelField("Legacy fallback (only when Outputs is empty)",EditorStyles.boldLabel);
            Fields(s,"targetIp","sendRateHz","sendAdm","admPort","admCoordinateMode","admSendRateHz","admSendOnlyOnChange","sendCustom","customPort","customAddressTemplate","customProtocol","monitorPort");s.ApplyModifiedProperties();
        }
        void AddOutput(Protocols.SpatialProtocol protocol)
        {
            Action(()=>{RoutingCsv.EnsureIdle(manager);Undo.RecordObject(manager,"Add protocol destination");manager.outputs.Add(new Protocols.SpatialOutput{key=Guid.NewGuid().ToString("N"),label=protocol+" output",protocol=protocol,ip=manager.targetIp,port=protocol==Protocols.SpatialProtocol.ADM?manager.admPort:manager.customPort});EditorUtility.SetDirty(manager);});
        }
        void Playback()
        {
            if(!NativeAudio.IsAvailable)EditorGUILayout.HelpBox(NativeAudio.AvailabilityError,MessageType.Warning);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Refresh native output devices (explicit)")){NativeAudio.Probe();NativeAudio.RefreshDevices();}
                if(GUILayout.Button("Stop all scene cues + replay"))foreach(var cue in SceneInventory.InWorld<PlaybackCue>(manager))cue.Stop();
                if(GUILayout.Button("Inspect all cues"))Selection.objects=SceneInventory.InWorld<PlaybackCue>(manager).Cast<UnityEngine.Object>().ToArray();
            }
            foreach(var cue in SceneInventory.InWorld<PlaybackCue>(manager))
            {
                EditorGUILayout.LabelField(cue.name+" — "+cue.WorkerState+(cue.ReplayPending?" / replay pending":""),EditorStyles.boldLabel);
                var s=new SerializedObject(cue);s.Update();Fields(s,"preparedAudio","clip","oscBase","configuredDeviceId","outputChannel","loop","autoReplay","replayDelaySeconds","exclusiveRequested","bufferMsRequested");s.ApplyModifiedProperties();
                using(new EditorGUILayout.HorizontalScope())
                {
                    if(GUILayout.Button("Prepare PCM"))cue.Prepare();if(GUILayout.Button("Play physical output"))cue.TryPlay();if(GUILayout.Button("Stop + cancel replay"))cue.Stop();if(GUILayout.Button("Release PCM"))cue.ReleaseAudio();
                }
                foreach(var device in NativeAudio.CachedDevices)if(GUILayout.Button($"Use {device.name} / {device.channels} ch")){Undo.RecordObject(cue,"Set output device");cue.configuredDeviceId=device.id;EditorUtility.SetDirty(cue);}
                EditorGUILayout.LabelField("Observed output",$"{cue.ActualOutputChannels} ch / {cue.ActualOutputRate} Hz / {cue.ActualBufferFrames} frames / exclusive {cue.ExclusiveActive}");
                if(!string.IsNullOrEmpty(cue.LastError))EditorGUILayout.HelpBox(cue.LastError,MessageType.Warning);EditorGUILayout.Space(10);
            }
        }
        void Csv()
        {
            EditorGUILayout.HelpBox("Preview → confirm → one Undo. All scene OSC/audio/replay must be stopped. Playback v2 exports exact GUID:localFileId source references; v1 files remain compatible and preserve sources. Import never starts native PCM/devices/transport.",MessageType.Info);
            if(GUILayout.Button("Export spatial route CSV"))RoutingCsvActions.Export(manager,false);
            if(GUILayout.Button("Export playback + source references CSV (v2)"))RoutingCsvActions.Export(manager,true);
            if(GUILayout.Button("Preview spatial import"))RoutingCsvActions.Import(manager,false);
            if(GUILayout.Button("Preview playback import (v1/v2)"))RoutingCsvActions.Import(manager,true);
        }
        void Diagnostics()
        {
            EditorGUILayout.LabelField("Sent / received",$"{manager.Sent} / {manager.Received}");
            if(!string.IsNullOrEmpty(manager.LastError))EditorGUILayout.HelpBox("Last reported error: "+manager.LastError,MessageType.Warning);
            var issues=SpatialAuthoring.Validate(manager);if(issues.Count==0)EditorGUILayout.HelpBox("Binding/output references validate. This is not physical renderer/audio certification.",MessageType.Info);else foreach(string issue in issues)EditorGUILayout.HelpBox(issue,MessageType.Warning);
            if(GUILayout.Button("Select manager"))Selection.activeGameObject=manager.gameObject;
            if(GUILayout.Button("Repair duplicate scene identities (Undo)"))SceneIdentityTools.RepairScene(manager.gameObject.scene);
        }
        void OnInspectorUpdate(){selected.RemoveWhere(o=>o==null);Repaint();}
    }
}
