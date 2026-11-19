// Hardware-free route and inventory regressions. MIT.
using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using NPLAudio.SpatialFabric.Audio;
using NPLAudio.SpatialFabric.Editor;
namespace NPLAudio.SpatialFabric.Tests
{
    public sealed class AudioRoutingPanelTests
    {
        static readonly DateTime Now=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc);
        static AudioDeviceInfo Device(string id="interface-a",int channels=8)=>new AudioDeviceInfo{id=id,name="Studio",channels=channels,sample_rate=48000};
        sealed class Inventory : IAudioRoutingInventory
        {
            public int probes,refreshes;
            public string error="";
            public AudioDeviceInfo[] devices={Device()};
            public void Probe(){probes++;}
            public AudioDeviceInfo[] RefreshDevices(){refreshes++;return devices;}
            public string DeviceEnumerationError=>error;
        }
        [Test] public void LoadIsOfflineReadOnlyAndNativeFreeUntilExplicitRefresh()
        {
            string json=null;int writes=0;
            var seed=new AudioInterfaceCache(()=>null,s=>json=s);
            Assert.That(seed.AcceptInventory(new[]{Device()},"",Now),Is.True);
            var cache=new AudioInterfaceCache(()=>json,s=>writes++);
            var native=new Inventory();var panel=new AudioRoutingPanel(cache,native);
            Assert.That(cache.Devices.Single().online,Is.False);
            Assert.That(writes,Is.Zero);Assert.That(native.probes,Is.Zero);Assert.That(native.refreshes,Is.Zero);
            Assert.That(panel.RefreshInterfaces(),Is.True);
            Assert.That(writes,Is.EqualTo(1));Assert.That(native.probes,Is.EqualTo(1));Assert.That(native.refreshes,Is.EqualTo(1));
            Assert.That(cache.Devices.Single().online,Is.True);
        }
        [Test] public void UnplugAndReconnectKeepHistoryAndCueAssignment()
        {
            var obj=new GameObject("Offline cue");
            try
            {
                var cue=obj.AddComponent<PlaybackCue>();cue.configuredDeviceId="interface-a";cue.outputChannel=7;
                var cache=new AudioInterfaceCache(()=>null,s=>{});
                Assert.That(cache.AcceptInventory(new[]{Device()},"",Now),Is.True);
                Assert.That(cache.AcceptInventory(Array.Empty<AudioDeviceInfo>(),"",Now.AddDays(1)),Is.True);
                var d=cache.Devices.Single();Assert.That(d.online,Is.False);Assert.That(d.channels,Is.EqualTo(8));Assert.That(d.lastSeenUtc,Is.EqualTo(Now.ToString("o")));
                Assert.That(AudioRoutingPanel.IsRoute(cue,"interface-a",7),Is.True);
                Assert.That(cache.AcceptInventory(new[]{Device(channels:16)},"",Now.AddDays(2)),Is.True);
                Assert.That(cache.Devices.Single().online,Is.True);Assert.That(cache.Devices.Single().channels,Is.EqualTo(16));
                Assert.That(AudioRoutingPanel.IsRoute(cue,"interface-a",7),Is.True);
            }
            finally{UnityEngine.Object.DestroyImmediate(obj);}
        }
        [Test] public void EnumerationFailureDoesNotPublishStaleInventoryOrWrite()
        {
            int writes=0;var cache=new AudioInterfaceCache(()=>null,s=>writes++);
            cache.AcceptInventory(new[]{Device()},"",Now);
            var native=new Inventory{error="Device list changed/exceeded bounds; refresh again."};
            Assert.That(new AudioRoutingPanel(cache,native).RefreshInterfaces(),Is.False);
            Assert.That(cache.Devices.Single().online,Is.False);Assert.That(writes,Is.EqualTo(1));
            Assert.That(cache.LastError,Does.Contain("Device list changed"));
        }
        [Test] public void InvalidInventoriesAreAtomicAndBounded()
        {
            int writes=0;var cache=new AudioInterfaceCache(()=>null,s=>writes++);
            cache.AcceptInventory(new[]{Device()},"",Now);
            foreach(var bad in new[]{new[]{Device(channels:0)},new[]{Device(channels:AudioInterfaceCache.MaxChannels+1)},new[]{Device("" )},new[]{Device(),Device()},new AudioDeviceInfo[]{null},new[]{Device(new string('x',AudioInterfaceCache.MaxTextLength+1))},Enumerable.Range(0,257).Select(i=>Device(i.ToString())).ToArray()})
                Assert.That(cache.AcceptInventory(bad,"",Now),Is.False);
            var rate=Device();rate.sample_rate=-1;Assert.That(cache.AcceptInventory(new[]{rate},"",Now),Is.False);
            Assert.That(writes,Is.EqualTo(1));Assert.That(cache.Devices.Single().id,Is.EqualTo("interface-a"));
            var copy=cache.Devices;copy[0].id="mutated";Assert.That(cache.Devices.Single().id,Is.EqualTo("interface-a"));
        }
        [Test] public void MalformedCacheIsIgnoredWithoutWrites()
        {
            int writes=0;
            foreach(string json in new[]{"invalid","{\"version\":99,\"devices\":[]}",new string('x',AudioInterfaceCache.MaxJsonLength+1)})
                Assert.That(new AudioInterfaceCache(()=>json,s=>writes++).Devices,Is.Empty);
            Assert.That(writes,Is.Zero);
        }
        [Test] public void UnknownAndDefaultRoutesAreDistinctAndUndoable()
        {
            var obj=new GameObject("Route cue");
            try
            {
                var cue=obj.AddComponent<PlaybackCue>();cue.configuredDeviceId="never-enumerated";cue.outputChannel=9000;cue.loop=true;cue.replayDelaySeconds=.37f;
                Assert.That(AudioRoutingPanel.IsRoute(cue,"never-enumerated",9000),Is.True);
                Assert.That(AudioRoutingPanel.IsRoute(cue,"",9000),Is.False);
                Assert.That(AudioRoutingPanel.AssignRoute(cue,"",0,out var reason),Is.True,reason);
                Undo.FlushUndoRecordObjects();Undo.PerformUndo();
                Assert.That(cue.configuredDeviceId,Is.EqualTo("never-enumerated"));Assert.That(cue.outputChannel,Is.EqualTo(9000));
                Undo.PerformRedo();Assert.That(AudioRoutingPanel.IsRoute(cue,"",0),Is.True);
                Assert.That(cue.loop,Is.True);Assert.That(cue.replayDelaySeconds,Is.EqualTo(.37f));
                Assert.That(AudioRoutingPanel.AssignRoute(cue,"offline",1,out reason),Is.True,reason);
                Assert.That(AudioRoutingPanel.IsRoute(cue,"offline",1),Is.True);
                Assert.That(AudioRoutingPanel.AssignRoute(cue,"offline",-1,out reason),Is.False);
                Assert.That(AudioRoutingPanel.ChannelLabel(0),Does.Contain("corresponding"));Assert.That(AudioRoutingPanel.ChannelLabel(1),Does.Contain("first source"));
            }
            finally{Undo.ClearAll();UnityEngine.Object.DestroyImmediate(obj);}
        }
        [Test] public void ActiveWorkerAndReplayGuardsAreConservative()
        {
            foreach(var state in new[]{AudioWorkerState.Starting,AudioWorkerState.Playing,AudioWorkerState.Draining})
                Assert.That(AudioRoutingPanel.IsStopped(state,false,false),Is.False);
            Assert.That(AudioRoutingPanel.IsStopped(default,true,false),Is.False);
            Assert.That(AudioRoutingPanel.IsStopped(default,false,true),Is.False);
            var obj=new GameObject("Active cue");
            try
            {
                var cue=obj.AddComponent<PlaybackCue>();cue.configuredDeviceId="original";cue.outputChannel=2;
                typeof(PlaybackCue).GetProperty("WorkerState").GetSetMethod(true).Invoke(cue,new object[]{AudioWorkerState.Starting});
                Assert.That(AudioRoutingPanel.AssignRoute(cue,"other",3,out _),Is.False);
                Assert.That(AudioRoutingPanel.IsRoute(cue,"original",2),Is.True);
            }
            finally{UnityEngine.Object.DestroyImmediate(obj);}
        }
        [Test] public void LockedAndDestroyedCuesRejectMutation()
        {
            var obj=new GameObject("Locked cue");var cue=obj.AddComponent<PlaybackCue>();
            try
            {
                cue.hideFlags=HideFlags.NotEditable;
                Assert.That(AudioRoutingPanel.AssignRoute(cue,"new",1,out _),Is.False);
                cue.hideFlags=HideFlags.None;obj.hideFlags=HideFlags.NotEditable;
                Assert.That(AudioRoutingPanel.AssignRoute(cue,"new",1,out _),Is.False);
            }
            finally{UnityEngine.Object.DestroyImmediate(obj);}
            Assert.That(AudioRoutingPanel.AssignRoute(cue,"new",1,out _),Is.False);
        }
    }
}
