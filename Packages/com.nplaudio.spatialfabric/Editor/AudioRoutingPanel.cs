// Offline-first route authoring. Drawing never probes, prepares or starts audio. MIT.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using NPLAudio.SpatialFabric.Audio;
namespace NPLAudio.SpatialFabric.Editor
{
    public interface IAudioRoutingInventory
    {
        void Probe();
        AudioDeviceInfo[] RefreshDevices();
        string DeviceEnumerationError {get;}
    }
    public sealed class AudioRoutingPanel
    {
        sealed class NativeInventory : IAudioRoutingInventory
        {
            public void Probe()=>NativeAudio.Probe();
            public AudioDeviceInfo[] RefreshDevices()=>NativeAudio.RefreshDevices();
            public string DeviceEnumerationError=>NativeAudio.DeviceEnumerationError;
        }
        const int CuesPerPage=16;
        sealed class Row
        {
            public PlaybackCue cue;public string device,reason;public int channel;public bool editable;
            public string[] deviceIds,deviceLabels,channelLabels;public int[] channelValues;public int deviceIndex,channelIndex;
        }
        readonly AudioInterfaceCache cache;
        readonly IAudioRoutingInventory inventory;
        readonly AudioRoutingMatrixView matrix=new AudioRoutingMatrixView();
        int cuePage,layoutCuePages=1;
        Row[] rows=Array.Empty<Row>();
        string message="",layoutMessage="",layoutError="";
        bool showMatrix=true;
        double nextPrune;
        AudioInterfaceCache.Snapshot[] layoutDevices=Array.Empty<AudioInterfaceCache.Snapshot>();
        readonly Dictionary<PlaybackCue,Row> dropdownRows=new Dictionary<PlaybackCue,Row>();
        readonly Dictionary<(PlaybackCue cue,string field),Rect> paintedDropdownScreens=new Dictionary<(PlaybackCue cue,string field),Rect>();
        Row DropdownSnapshot(PlaybackCue cue,AudioInterfaceCache.Snapshot[] snapshots)
        {
            bool editable=CanAssign(cue,out string reason);
            var row=new Row{cue=cue,device=cue.configuredDeviceId??"",channel=cue.outputChannel,editable=editable,reason=reason};
            var ids=new List<string>{""};var labels=new List<string>{"Dynamic OS default (not a fixed interface)"};
            var duplicates=new HashSet<string>(snapshots.GroupBy(d=>d.name,StringComparer.Ordinal).Where(g=>g.Count()>1).Select(g=>g.Key),StringComparer.Ordinal);
            foreach(var d in snapshots){ids.Add(d.id);labels.Add((string.IsNullOrEmpty(d.name)?d.id:d.name)+(duplicates.Contains(d.name)?" ["+d.id+"]":"")+" · "+d.channels+" ch"+(d.online?"":" · OFFLINE / cached"));}
            if(!ids.Contains(row.device)){ids.Add(row.device);labels.Add(row.device+" · UNKNOWN / offline");}
            row.deviceIds=ids.ToArray();row.deviceLabels=labels.ToArray();row.deviceIndex=ids.IndexOf(row.device);
            int capacity=snapshots.FirstOrDefault(d=>d.id==row.device)?.channels??0;
            var values=Enumerable.Range(0,capacity+1).ToList();var channels=values.Select(c=>c==0?"0 — corresponding source channels":"Output "+c).ToList();
            if(!values.Contains(row.channel)){values.Add(row.channel);channels.Add(row.channel+" — stored ("+(capacity==0?"capacity unknown":"outside cached capacity")+")");}
            row.channelValues=values.ToArray();row.channelLabels=channels.ToArray();row.channelIndex=values.IndexOf(row.channel);return row;
        }
        public void DrawRouteSelectors(PlaybackCue cue)
        {
            if(cue==null)return;
            if(Event.current.type==EventType.Layout){PruneDropdowns();dropdownRows[cue]=DropdownSnapshot(cue,cache.Devices);}
            if(dropdownRows.TryGetValue(cue,out var row))DrawDropdownRow(row);
        }
        void DrawDropdownRow(Row row)
        {
            using(new EditorGUI.DisabledScope(!row.editable))
            {
                int device=EditorGUILayout.Popup(new GUIContent("Audio interface",row.deviceIds[row.deviceIndex]),row.deviceIndex,row.deviceLabels);
                if(Event.current.type==EventType.Repaint)paintedDropdownScreens[(row.cue,"device")]=GUIUtility.GUIToScreenRect(GUILayoutUtility.GetLastRect());
                if(device!=row.deviceIndex)Apply(row.cue,row.deviceIds[device],row.channel);
                int channel=EditorGUILayout.Popup("Output channel",row.channelIndex,row.channelLabels);
                if(Event.current.type==EventType.Repaint)paintedDropdownScreens[(row.cue,"channel")]=GUIUtility.GUIToScreenRect(GUILayoutUtility.GetLastRect());
                if(channel!=row.channelIndex)Apply(row.cue,row.device,row.channelValues[channel]);
            }
            if(!row.editable)EditorGUILayout.LabelField(row.reason,EditorStyles.miniLabel);
        }
        public void DrawInventoryStatus()=>EditorGUILayout.LabelField("Interface inventory",string.IsNullOrEmpty(cache.LastError)?"Last explicit refresh succeeded (cached)":cache.LastError);
        public void DrawRefreshButton()
        {
            if(GUILayout.Button("Refresh Interfaces",GUILayout.Width(160))){RefreshInterfaces();GUIUtility.ExitGUI();}
        }
        void PruneDropdowns()
        {
            if(EditorApplication.timeSinceStartup<nextPrune)return;
            nextPrune=EditorApplication.timeSinceStartup+0.25;
            foreach(var cue in dropdownRows.Keys.Where(c=>c==null).ToArray())dropdownRows.Remove(cue);
        }
        public AudioRoutingPanel():this(new AudioInterfaceCache(),new NativeInventory()){}
        public AudioRoutingPanel(AudioInterfaceCache cache,IAudioRoutingInventory inventory)
        {this.cache=cache??throw new ArgumentNullException(nameof(cache));this.inventory=inventory??throw new ArgumentNullException(nameof(inventory));}
        public bool RefreshInterfaces()
        {
            try
            {
                inventory.Probe();
                var devices=inventory.RefreshDevices();
                return cache.AcceptInventory(devices,inventory.DeviceEnumerationError,DateTime.UtcNow);
            }
            catch(Exception e){return cache.AcceptInventory(null,"Interface refresh failed: "+e.Message,DateTime.UtcNow);}
        }
        public static string ChannelLabel(int channel)=>channel==0?"0 · corresponding":channel+" · first source";
        public static bool IsRoute(PlaybackCue cue,string deviceId,int channel)=>cue!=null && (cue.configuredDeviceId??"")== (deviceId??"") && cue.outputChannel==channel;
        public static bool IsStopped(AudioWorkerState state,bool playing,bool replayPending)=>!playing && !replayPending && state!=AudioWorkerState.Starting && state!=AudioWorkerState.Playing && state!=AudioWorkerState.Draining;
        public static bool CanAssign(PlaybackCue cue,out string reason)
        {
            reason="";
            if(cue==null){reason="Cue is missing or destroyed.";return false;}
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {reason="Routing requires stopped Edit Mode (not compiling/importing).";return false;}
            if(!IsStopped(cue.WorkerState,cue.IsPlaying,cue.ReplayPending)){reason="Stop this cue and pending replay before changing its route.";return false;}
            var obj=cue.gameObject;var scene=obj.scene;
            if(EditorUtility.IsPersistent(cue) || !scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene) || StageUtility.GetStageHandle(obj)!=StageUtility.GetMainStageHandle())
            {reason="Only loaded main-stage scene cues can be routed.";return false;}
            bool loaded=false;
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i)==scene)loaded=true;
            if(!loaded){reason="Cue is outside the loaded authoring scenes.";return false;}
            if((cue.hideFlags & (HideFlags.NotEditable|HideFlags.DontSaveInEditor))!=0 || PrefabUtility.IsPartOfImmutablePrefab(cue))
            {reason="Cue is locked or immutable.";return false;}
            for(var t=obj.transform;t!=null;t=t.parent)
                if((t.gameObject.hideFlags & (HideFlags.NotEditable|HideFlags.DontSaveInEditor))!=0){reason="Cue hierarchy is locked or temporary.";return false;}
            if(!string.IsNullOrEmpty(scene.path) && !AssetDatabase.IsOpenForEdit(scene.path))
            {reason="Scene is read-only or not open for edit.";return false;}
            return true;
        }
        // Fresh preflight at the mutation boundary; never resolves an empty ID to a fixed endpoint.
        public static bool AssignRoute(PlaybackCue cue,string deviceId,int channel,out string reason)
        {
            if(!CanAssign(cue,out reason))return false;
            if(channel<0 || deviceId!=null && (deviceId.Length>AudioInterfaceCache.MaxTextLength || deviceId.Any(char.IsControl)))
            {reason="Invalid route ID/channel.";return false;}
            if(IsRoute(cue,deviceId,channel))return false;
            Undo.RecordObject(cue,"Assign audio output route");
            cue.configuredDeviceId=deviceId??"";cue.outputChannel=channel;
            PrefabUtility.RecordPrefabInstancePropertyModifications(cue);
            EditorUtility.SetDirty(cue);EditorSceneManager.MarkSceneDirty(cue.gameObject.scene);
            return true;
        }
        void Capture(PlaybackCue[] cues)
        {
            var all=(cues??Array.Empty<PlaybackCue>()).Where(c=>c!=null).Distinct().ToArray();
            layoutCuePages=Math.Max(1,(all.Length+CuesPerPage-1)/CuesPerPage);cuePage=Mathf.Clamp(cuePage,0,layoutCuePages-1);
            layoutDevices=cache.Devices;
            rows=showMatrix?Array.Empty<Row>():all.Skip(cuePage*CuesPerPage).Take(CuesPerPage).Select(c=>DropdownSnapshot(c,layoutDevices)).ToArray();
            PruneDropdowns();
            layoutMessage=message;layoutError=cache.LastError;
        }
        public void Draw(PlaybackCue[] cues,Rect visibleScreen)
        {
            // Presentation is immutable across Layout/input/Repaint. Mutations still re-check the live cue.
            if(Event.current.type==EventType.Layout)Capture(cues);
            if(Event.current.type==EventType.Repaint)paintedDropdownScreens.Clear();
            DrawRefreshButton();
            EditorGUILayout.HelpBox("Inventory only: refresh never opens streams. Cached/offline routes remain assignable. 0 = corresponding source/output channels; 1+ = output channel receiving the first source channel.",MessageType.Info);
            if(!string.IsNullOrEmpty(layoutError))EditorGUILayout.HelpBox(layoutError,MessageType.Warning);
            if(!string.IsNullOrEmpty(layoutMessage))EditorGUILayout.HelpBox(layoutMessage,MessageType.Warning);
            int view=GUILayout.Toolbar(showMatrix?0:1,new[]{"Routing grid","Interface / channel dropdowns"});
            if((view==0)!=showMatrix){showMatrix=view==0;GUIUtility.ExitGUI();}
            if(showMatrix){matrix.Draw(cues,layoutDevices,visibleScreen);return;}
            int requested=Page("Cue rows",cuePage,layoutCuePages);
            if(requested!=cuePage){cuePage=requested;GUIUtility.ExitGUI();}
            if(rows.Length==0)EditorGUILayout.HelpBox("No playback cues in loaded scenes. Add PlaybackCue through Scene Objects to create routing rows.",MessageType.Info);
            foreach(var row in rows)
            {
                using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.ObjectField("Playback cue",row.cue,typeof(PlaybackCue),true);
                    DrawDropdownRow(row);
                }
            }
        }
        static int Page(string label,int page,int count)
        {
            page=Mathf.Clamp(page,0,count-1);
            using(new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label+" "+(page+1)+" / "+count,GUILayout.Width(170));
                using(new EditorGUI.DisabledScope(page==0))if(GUILayout.Button("Previous",GUILayout.Width(70)))page--;
                using(new EditorGUI.DisabledScope(page>=count-1))if(GUILayout.Button("Next",GUILayout.Width(50)))page++;
            }
            return page;
        }
        void Apply(PlaybackCue cue,string id,int channel){AssignRoute(cue,id,channel,out message);GUIUtility.ExitGUI();}
    }
}
