// Project-local, inventory-only audio endpoint history. No native calls on load. MIT.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using NPLAudio.SpatialFabric.Audio;
namespace NPLAudio.SpatialFabric.Editor
{
    public sealed class AudioInterfaceCache
    {
        public const int MaxDevices=256, MaxChannels=4096, MaxTextLength=1024, MaxJsonLength=2097152;
        const string Key="SpatialFabric.AudioInterfaces.v1";
        [Serializable] public sealed class Snapshot
        {
            public string id,name,lastSeenUtc;
            public int channels,sampleRate;
            // Session-only: deserializing a cache must never claim that hardware is online.
            [NonSerialized] public bool online;
            public Snapshot Copy()=>new Snapshot{id=id,name=name,lastSeenUtc=lastSeenUtc,channels=channels,sampleRate=sampleRate,online=online};
        }
        [Serializable] sealed class Document { public int version=1; public Snapshot[] devices; }
        readonly Action<string> save;
        readonly List<Snapshot> devices=new List<Snapshot>();
        public Snapshot[] Devices=>devices.Select(d=>d.Copy()).ToArray();
        public string LastError {get;private set;}="Not refreshed this session.";
        public AudioInterfaceCache():this(()=>EditorUserSettings.GetConfigValue(Key),s=>EditorUserSettings.SetConfigValue(Key,s)){}
        public AudioInterfaceCache(Func<string> load,Action<string> save)
        {
            this.save=save??throw new ArgumentNullException(nameof(save));
            try
            {
                string json=load?.Invoke();
                if(string.IsNullOrEmpty(json))return;
                if(json.Length>MaxJsonLength)throw new ArgumentException("Audio interface cache exceeds size bounds.");
                var doc=JsonUtility.FromJson<Document>(json);
                if(doc==null || doc.version!=1 || doc.devices==null || doc.devices.Length>MaxDevices)throw new ArgumentException("Unsupported audio interface cache schema.");
                var ids=new HashSet<string>(StringComparer.Ordinal);
                foreach(var d in doc.devices)
                {
                    if(!Valid(d) || !ids.Add(d.id))throw new ArgumentException("Invalid audio interface cache entry.");
                    var copy=d.Copy();copy.online=false;devices.Add(copy);
                }
            }
            catch(Exception e){devices.Clear();LastError="Cache ignored: "+e.Message;}
        }
        static bool Text(string s,bool required)=>s!=null && s.Length<=MaxTextLength && (!required || s.Length>0) && !s.Any(char.IsControl);
        static bool Valid(Snapshot d)=>d!=null && Text(d.id,true) && Text(d.name,false) && d.channels>0 && d.channels<=MaxChannels && d.sampleRate>=0 && d.sampleRate<=768000 && Text(d.lastSeenUtc,false);
        // Atomic validation: failed enumeration (including stale NativeAudio.CachedDevices) is never saved.
        public bool AcceptInventory(AudioDeviceInfo[] inventory,string enumerationError,DateTime utcNow)
        {
            foreach(var d in devices)d.online=false;
            if(!string.IsNullOrEmpty(enumerationError)){LastError=enumerationError;return false;}
            if(inventory==null || inventory.Length>MaxDevices){LastError="Invalid audio inventory size.";return false;}
            string seen=utcNow.ToUniversalTime().ToString("o");
            var fresh=new List<Snapshot>();var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var d in inventory)
            {
                var s=d==null?null:new Snapshot{id=d.id,name=d.name,channels=d.channels,sampleRate=d.sample_rate,lastSeenUtc=seen,online=true};
                if(!Valid(s) || !ids.Add(s.id)){LastError="Invalid audio inventory entry or duplicate endpoint ID.";return false;}
                fresh.Add(s);
            }
            var merged=fresh.Concat(devices.Where(d=>!ids.Contains(d.id))).Select(d=>d.Copy()).ToArray();
            if(merged.Length>MaxDevices){LastError="Audio interface history is full; refresh was not cached.";return false;}
            // Save before publishing, so a persistence failure does not partially replace history.
            try{save(JsonUtility.ToJson(new Document{devices=merged}));}
            catch(Exception e){LastError="Could not save audio interface cache: "+e.Message;return false;}
            devices.Clear();devices.AddRange(merged);LastError="";return true;
        }
    }
}
