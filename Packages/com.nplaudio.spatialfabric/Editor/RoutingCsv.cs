// Scene-local, validate-first authoring transactions. No preparation, device enumeration or transport. MIT.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using NPLAudio.SpatialFabric.Protocols;
using NPLAudio.SpatialFabric.Routing;
namespace NPLAudio.SpatialFabric.Editor
{
    public sealed class RoutingImportPlan
    {
        internal sealed class Change { public SpatialIdentity target;public string key,before;public Action apply,resolve; }
        readonly SpatialFabricManager manager;
        readonly string managerBefore;
        readonly List<Change> changes;
        public int Count=>changes.Count;
        public string Description {get;private set;}
        internal void AttachAssets(string key,Action resolve,Action assign)
        {
            var change=changes.Single(c=>c.key==key);change.resolve=resolve;change.apply+=assign;
            Description="Playback routing + explicit source-asset reference assignment. All references resolve before one Undo transaction; native PCM/devices/transport are not started.";
        }
        internal RoutingImportPlan(SpatialFabricManager owner,List<Change> rows,string description) {manager=owner;managerBefore=EditorJsonUtility.ToJson(owner);changes=rows;Description=description;}
        public void Apply()
        {
            RoutingCsv.EnsureIdle(manager);
            if(EditorJsonUtility.ToJson(manager)!=managerBefore)throw new InvalidOperationException("Manager changed since preview; import again");
            foreach(var change in changes)
                if(change.target==null || !manager.ContainsScene(change.target.gameObject.scene) || change.target.Key!=change.key || EditorJsonUtility.ToJson(change.target)!=change.before)throw new InvalidOperationException("Route/identity changed since preview; nothing was applied");
            foreach(var change in changes)change.resolve?.Invoke();
            RoutingCsv.EnsureIdle(manager);if(EditorJsonUtility.ToJson(manager)!=managerBefore)throw new InvalidOperationException("Asset resolution changed manager configuration; import again");
            foreach(var change in changes)if(change.target==null || !manager.ContainsScene(change.target.gameObject.scene) || EditorJsonUtility.ToJson(change.target)!=change.before)throw new InvalidOperationException("Asset resolution changed a target; import again before applying");
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Import SpatialFabric routing");
            try
            {
                Undo.RegisterCompleteObjectUndo(changes.Select(c=>(UnityEngine.Object)c.target).ToArray(),"Import SpatialFabric routing");
                foreach(var change in changes){change.apply();PrefabUtility.RecordPrefabInstancePropertyModifications(change.target);EditorUtility.SetDirty(change.target);}
                foreach(var scene in changes.Select(c=>c.target.gameObject.scene).Distinct())EditorSceneManager.MarkSceneDirty(scene);Undo.CollapseUndoOperations(group);
            }
            catch {Undo.RevertAllDownToGroup(group);throw;}
        }
    }
    public static class RoutingCsv
    {
        public const string SpatialSchema="sf-unity-spatial-routing/1",PlaybackSchema="sf-unity-playback-routing/1";
        public static readonly string[] SpatialHeader={"Schema","Key","ActorPath","ObjectId","CustomObjectId","SendAdm","SendCustom","Gain","Width","Mute","CustomFields"};
        public static readonly string[] PlaybackHeader={"Schema","Key","ActorPath","DeviceId","OutputChannel","OscBase","Loop","AutoReplay","ReplayDelaySeconds","ExclusiveRequested","BufferMsRequested"};
        [Serializable] sealed class FieldList { public List<CustomField> entries; }
        public static void EnsureIdle(SpatialFabricManager manager)
        {
            if(manager==null || !manager.gameObject.scene.IsValid() || !manager.gameObject.scene.isLoaded || EditorUtility.IsPersistent(manager))throw new InvalidOperationException("Choose a loaded-scene manager");
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play mode before routing import");
            var scene=manager.gameObject.scene;
            if(SceneInventory.InWorld<SpatialFabricManager>(manager).Any(m=>m.IsListening))throw new InvalidOperationException("Stop all scene OSC managers before routing import; no sockets were stopped automatically");
            if(SceneInventory.InWorld<PlaybackCue>(manager).Any(c=>c.IsPlaying || c.WorkerState==Audio.AudioWorkerState.Starting || c.WorkerState==Audio.AudioWorkerState.Draining || c.ReplayPending))throw new InvalidOperationException("Stop audio and pending replay explicitly before routing import");
        }
        static List<T> Unique<T>(IEnumerable<T> items) where T:SpatialIdentity
        {
            var list=items.Where(i=>i!=null).Distinct().OrderBy(i=>i.Key,StringComparer.Ordinal).ToList();
            if(list.GroupBy(i=>i.Key,StringComparer.Ordinal).Any(g=>g.Count()!=1))throw new FormatException("Duplicate scene identities; repair them explicitly before CSV authoring");return list;
        }
        static string Bool(bool value)=>value?"true":"false";
        static string Number(float value)=>value.ToString("R",CultureInfo.InvariantCulture);
        static bool Boolean(string text)
        {
            if(text=="1" || string.Equals(text,"true",StringComparison.OrdinalIgnoreCase))return true;
            if(text=="0" || string.Equals(text,"false",StringComparison.OrdinalIgnoreCase))return false;throw new FormatException("Invalid routing boolean");
        }
        static int Integer(string text,int min,int max)
        {
            if(!int.TryParse(text,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out int value) || value<min || value>max)throw new FormatException("Invalid routing integer/range: "+text);return value;
        }
        static float Scalar(string text,float min,float max)
        {
            if(!float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out float value) || !CustomProtocol.Finite(value) || value<min || value>max)throw new FormatException("Invalid finite routing scalar: "+text);return value;
        }
        public static string ExportSpatial(SpatialFabricManager manager)
        {
            if(manager==null)throw new ArgumentNullException(nameof(manager));var rows=new List<string[]>{SpatialHeader};
            foreach(var obj in Unique(manager.objects.Where(o=>o!=null && manager.ContainsScene(o.gameObject.scene))))
            {
                CustomProtocol.ValidateFields(obj.customFields);
                rows.Add(new[]{SpatialSchema,obj.Key,SceneInventory.Path(obj.transform),obj.objectId.ToString(CultureInfo.InvariantCulture),obj.customObjectIdOverride.ToString(CultureInfo.InvariantCulture),Bool(obj.sendAdm),Bool(obj.sendCustom),Number(obj.gain),Number(obj.width),Bool(obj.mute),JsonUtility.ToJson(new FieldList{entries=obj.customFields})});
            }
            return RoutingCsvCodec.Write(rows);
        }
        public static string ExportPlayback(SpatialFabricManager manager)
        {
            if(manager==null)throw new ArgumentNullException(nameof(manager));var rows=new List<string[]>{PlaybackHeader};
            foreach(var cue in Unique(SceneInventory.InWorld<PlaybackCue>(manager)))
                rows.Add(new[]{PlaybackSchema,cue.Key,SceneInventory.Path(cue.transform),cue.configuredDeviceId,cue.outputChannel.ToString(CultureInfo.InvariantCulture),cue.oscBase,Bool(cue.loop),Bool(cue.autoReplay),Number(cue.replayDelaySeconds),Bool(cue.exclusiveRequested),cue.bufferMsRequested.ToString(CultureInfo.InvariantCulture)});
            return RoutingCsvCodec.Write(rows);
        }
        public static RoutingImportPlan PreviewSpatial(SpatialFabricManager manager,string text)
        {
            EnsureIdle(manager);var targets=Unique(manager.objects.Where(o=>o!=null && manager.ContainsScene(o.gameObject.scene))).ToDictionary(o=>o.Key,StringComparer.Ordinal);
            return Preview(manager,text,SpatialHeader,SpatialSchema,(row,get)=>
            {
                string key=get("Key");if(!targets.TryGetValue(key,out var target))throw new FormatException("Unknown bound spatial key (no label fallback): "+key);
                int id=Integer(get("ObjectId"),1,int.MaxValue),custom=Integer(get("CustomObjectId"),-1,int.MaxValue);if(custom==0)throw new FormatException("Custom ID is -1 (inherit) or positive");
                bool adm=Boolean(get("SendAdm")),customEnabled=Boolean(get("SendCustom")),mute=Boolean(get("Mute"));float gain=Scalar(get("Gain"),0,2),width=Scalar(get("Width"),0,1);
                FieldList fields;try{fields=JsonUtility.FromJson<FieldList>(get("CustomFields"));}catch(Exception e){throw new FormatException("Invalid custom field JSON",e);}
                CustomProtocol.ValidateFields(fields?.entries);
                return Change(target,()=>{target.objectId=id;target.customObjectIdOverride=custom;target.sendAdm=adm;target.sendCustom=customEnabled;target.gain=gain;target.width=width;target.mute=mute;target.customFields=fields.entries.Select(f=>new CustomField{key=f.key,value=f.value}).ToList();});
            });
        }
        public static RoutingImportPlan PreviewPlayback(SpatialFabricManager manager,string text)
        {
            EnsureIdle(manager);var targets=Unique(SceneInventory.InWorld<PlaybackCue>(manager)).ToDictionary(c=>c.Key,StringComparer.Ordinal);
            return Preview(manager,text,PlaybackHeader,PlaybackSchema,(row,get)=>
            {
                string key=get("Key");if(!targets.TryGetValue(key,out var target))throw new FormatException("Unknown playback key (no label fallback): "+key);
                string device=get("DeviceId"),osc=get("OscBase");if(device.IndexOf('\0')>=0 || !string.IsNullOrWhiteSpace(osc) && !Osc.OscCodec.ValidAddress(osc))throw new FormatException("Invalid device ID or explicit OSC base");
                int channel=Integer(get("OutputChannel"),0,64),buffer=Integer(get("BufferMsRequested"),0,2000);float delay=Scalar(get("ReplayDelaySeconds"),0,86400);
                bool loop=Boolean(get("Loop")),replay=Boolean(get("AutoReplay")),exclusive=Boolean(get("ExclusiveRequested"));
                return Change(target,()=>{target.configuredDeviceId=device;target.outputChannel=channel;target.oscBase=osc;target.loop=loop;target.autoReplay=replay;target.replayDelaySeconds=delay;target.exclusiveRequested=exclusive;target.bufferMsRequested=buffer;});
            });
        }
        static RoutingImportPlan.Change Change(SpatialIdentity target,Action action)=>new RoutingImportPlan.Change {target=target,key=target.Key,before=EditorJsonUtility.ToJson(target),apply=action};
        static RoutingImportPlan Preview(SpatialFabricManager manager,string text,string[] header,string schema,Func<string[],Func<string,string>,RoutingImportPlan.Change> parse)
        {
            var rows=RoutingCsvCodec.Parse(text);if(rows.Count<2)throw new FormatException("CSV has no routes");var map=RoutingCsvCodec.Header(rows[0],header);
            var keys=new HashSet<string>(StringComparer.Ordinal);var changes=new List<RoutingImportPlan.Change>();
            for(int i=1;i<rows.Count;++i)
            {
                string[] row=rows[i];if(row.Length!=header.Length)throw new FormatException("Incomplete/extra CSV fields in record "+(i+1));
                string Get(string name)=>row[map[name]];
                if(Get("Schema")!=schema || !keys.Add(Get("Key")))throw new FormatException("Wrong schema or duplicate routing key in record "+(i+1));
                changes.Add(parse(row,Get));
            }
            return new RoutingImportPlan(manager,changes,$"{schema}: {changes.Count} routes validated. Keys resolve within the selected scene; no assets loaded, devices queried, sockets started or audio prepared.");
        }
    }
}
