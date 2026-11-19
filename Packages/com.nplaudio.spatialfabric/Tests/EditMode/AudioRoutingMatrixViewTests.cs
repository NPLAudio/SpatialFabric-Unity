// Hardware-free crosspoint geometry and presentation regressions. MIT.
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using NPLAudio.SpatialFabric.Audio;
using NPLAudio.SpatialFabric.Editor;

namespace NPLAudio.SpatialFabric.Tests
{
    public sealed class AudioRoutingMatrixViewTests
    {
        readonly List<GameObject> objects=new List<GameObject>();
        PlaybackCue Cue(string name,string id="",int channel=0)
        {
            var obj=new GameObject(name);objects.Add(obj);
            var cue=obj.AddComponent<PlaybackCue>();cue.configuredDeviceId=id;cue.outputChannel=channel;return cue;
        }
        static AudioInterfaceCache.Snapshot Device(string id,int channels=8,string name="Studio",bool online=false)=>
            new AudioInterfaceCache.Snapshot{id=id,name=name,channels=channels,sampleRate=48000,lastSeenUtc="2026-01-01T00:00:00Z",online=online};
        [TearDown] public void Cleanup()
        {
            foreach(var obj in objects)if(obj!=null)UnityEngine.Object.DestroyImmediate(obj);
            objects.Clear();
        }
        [Test] public void ConstructorDoesNotRequireGuiOrInventory()
        {
            Assert.DoesNotThrow(()=>new AudioRoutingMatrixView());
        }
        [Test] public void KnownInterfaceHasAutoThenEveryOneBasedOutput()
        {
            var p=AudioRoutingMatrixView.Capture(new[]{Cue("Cue","a",3)},new[]{Device("a",4)});
            Assert.That(p.ColumnCount,Is.EqualTo(6)); // dynamic Auto + fixed Auto + 1..4
            Assert.That(p.Groups[1].Start,Is.EqualTo(1));
            Assert.That(Enumerable.Range(1,5).Select(i=>p.ColumnAt(i).Channel),Is.EqualTo(new[]{0,1,2,3,4}));
            Assert.That(p.ColumnAt(1).Label,Is.EqualTo("Auto"));
            Assert.That(p.ColumnAt(1).Tooltip,Does.Contain("corresponding"));
            Assert.That(p.ColumnAt(2).Tooltip,Does.Contain("first source"));
            Assert.Throws<ArgumentOutOfRangeException>(()=>p.ColumnAt(-1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>p.ColumnAt(p.ColumnCount));
            Assert.Throws<ArgumentOutOfRangeException>(()=>p.Groups[1].ChannelAt(5));
        }
        [Test] public void OfflineCacheRetainsFullCapacityAndNameWithoutWrites()
        {
            string json=null;
            var seed=new AudioInterfaceCache(()=>null,s=>json=s);
            seed.AcceptInventory(new[]{new AudioDeviceInfo{id="offline-id",name="Desk outputs",channels=64,sample_rate=48000}},"",DateTime.UtcNow);
            int writes=0;var cache=new AudioInterfaceCache(()=>json,s=>writes++);
            var p=AudioRoutingMatrixView.Capture(new[]{Cue("Cue","offline-id",64)},cache.Devices);
            Assert.That(p.Groups[1].Count,Is.EqualTo(65));
            Assert.That(p.Groups[1].Online,Is.False);
            Assert.That(p.Groups[1].Title,Does.Contain("Desk outputs").And.Contain("OFFLINE / cached"));
            Assert.That(p.ColumnAt(p.ColumnCount-1).Channel,Is.EqualTo(64));
            Assert.That(writes,Is.Zero);
        }
        [Test] public void UnknownCapacityDoesNotInventIntermediateChannels()
        {
            var cues=new[]{Cue("A","missing",9000),Cue("B","missing",4),Cue("C","missing",9000)};
            var p=AudioRoutingMatrixView.Capture(cues,Array.Empty<AudioInterfaceCache.Snapshot>());
            Assert.That(p.EmptyCache,Is.True);
            Assert.That(p.Groups[1].Known,Is.False);
            Assert.That(p.Groups[1].Capacity,Is.Zero);
            Assert.That(p.Groups[1].Count,Is.EqualTo(3));
            Assert.That(Enumerable.Range(0,p.Groups[1].Count).Select(p.Groups[1].ChannelAt),Is.EqualTo(new[]{0,4,9000}));
            Assert.That(p.ColumnAt(3).Label,Is.EqualTo("9000*"));
            Assert.That(p.ColumnAt(3).Tooltip,Does.Contain("capacity unknown").And.Contain("stored route"));
            Assert.That(CheckedCount(p,p.Rows[0]),Is.EqualTo(1));
        }
        [Test] public void DynamicDefaultIsNotTheHardwareDefaultAndOnlyShowsStoredPositives()
        {
            var p=AudioRoutingMatrixView.Capture(new[]{Cue("Dynamic","",7),Cue("Fixed","hardware-default",7)},new[]{Device("hardware-default",8)});
            var dynamic=p.Groups[0];
            Assert.That(dynamic.DeviceId,Is.Empty);Assert.That(dynamic.Known,Is.False);
            Assert.That(dynamic.Capacity,Is.Zero);Assert.That(dynamic.StoredChannels,Is.EqualTo(new[]{7}));
            Assert.That(dynamic.Tooltip,Does.Contain("not a fixed hardware endpoint"));
            Assert.That(p.Groups[1].DeviceId,Is.EqualTo("hardware-default"));
            foreach(var row in p.Rows)Assert.That(CheckedCount(p,row),Is.EqualTo(1));
        }
        [Test] public void OutOfRangeStoredRouteRemainsRepresentableWithoutInventedCapacity()
        {
            var p=AudioRoutingMatrixView.Capture(new[]{Cue("Cue","a",9000)},new[]{Device("a",2)});
            Assert.That(p.Groups[1].Capacity,Is.EqualTo(2));
            Assert.That(p.Groups[1].Count,Is.EqualTo(4));
            var last=p.ColumnAt(p.ColumnCount-1);
            Assert.That(last.Channel,Is.EqualTo(9000));Assert.That(last.Stored,Is.True);
            Assert.That(last.Tooltip,Does.Contain("outside cached capacity"));
            Assert.That(CheckedCount(p,p.Rows[0]),Is.EqualTo(1));
        }
        [Test] public void ExactOrdinalIdsAndDuplicateNamesAreDistinguished()
        {
            var p=AudioRoutingMatrixView.Capture(new[]{Cue("Upper","ABC",1),Cue("Lower","abc",1),Cue("Unknown","Abc",1)},new[]{Device("ABC",2),Device("abc",3)});
            Assert.That(p.Groups.Select(g=>g.DeviceId),Is.EqualTo(new[]{"","ABC","abc","Abc"}));
            Assert.That(p.Groups[1].Title,Does.Contain("[ABC]"));
            Assert.That(p.Groups[2].Title,Does.Contain("[abc]"));
            Assert.That(p.Groups[3].Known,Is.False);
            foreach(var row in p.Rows)Assert.That(CheckedCount(p,row),Is.EqualTo(1));
        }
        [Test] public void PresentationSurvivesRouteWorkerNameAndDestructionChanges()
        {
            var cue=Cue("Original","a",2);var device=Device("a",8);
            var p=AudioRoutingMatrixView.Capture(new[]{cue},new[]{device});
            cue.configuredDeviceId="new";cue.outputChannel=9000;cue.name="Changed";
            typeof(PlaybackCue).GetProperty("WorkerState").GetSetMethod(true).Invoke(cue,new object[]{AudioWorkerState.Starting});
            device.channels=1;device.name="Changed device";
            Assert.That(p.Rows[0].Name,Is.EqualTo("Original"));
            Assert.That(p.Rows[0].DeviceId,Is.EqualTo("a"));Assert.That(p.Rows[0].Channel,Is.EqualTo(2));
            Assert.That(p.Groups[1].Capacity,Is.EqualTo(8));
            Assert.That(p.Groups[1].Title,Does.Contain("Studio"));
            var fresh=AudioRoutingMatrixView.Capture(new[]{cue},new[]{device});
            Assert.That(fresh.Rows[0].Editable,Is.False);
            UnityEngine.Object.DestroyImmediate(cue.gameObject);
            Assert.That(p.Rows.Length,Is.EqualTo(1));Assert.That(p.Rows[0].Name,Is.EqualTo("Original"));
            Assert.That(AudioRoutingMatrixView.Capture(new[]{cue},new[]{device}).Rows,Is.Empty);
        }
        [Test] public void RowsRetainNamesAndSceneHierarchyInTooltip()
        {
            var parent=new GameObject("Parent");objects.Add(parent);
            var cue=Cue("Child");cue.transform.SetParent(parent.transform);
            var p=AudioRoutingMatrixView.Capture(new[]{cue,cue,null},null);
            Assert.That(p.Rows.Length,Is.EqualTo(1));Assert.That(p.Rows[0].Name,Is.EqualTo("Child"));
            Assert.That(p.Rows[0].Tooltip,Does.Contain("Parent/Child"));
            Assert.That(p.Rows[0].Tooltip,Does.Contain(cue.gameObject.scene.name));
        }
        [Test] public void AllRowsAndInterfacesRemainAvailableWithoutFoldoutsOrPages()
        {
            var cues=Enumerable.Range(0,40).Select(i=>Cue("Cue "+i,"id-"+i,1)).ToArray();
            var devices=Enumerable.Range(0,40).Select(i=>Device("id-"+i,32)).ToArray();
            var p=AudioRoutingMatrixView.Capture(cues,devices);
            Assert.That(p.Rows.Length,Is.EqualTo(40));Assert.That(p.Groups.Length,Is.EqualTo(41));
            Assert.That(p.Rows[39].Name,Is.EqualTo("Cue 39"));
            Assert.That(p.ColumnAt(p.ColumnCount-1).Group.DeviceId,Is.EqualTo("id-39"));
            Assert.That(p.ColumnAt(p.ColumnCount-1).Channel,Is.EqualTo(32));
        }
        [Test] public void EmptyInputsStillExposeDynamicAutoColumn()
        {
            var p=AudioRoutingMatrixView.Capture(null,null);
            Assert.That(p.Rows,Is.Empty);Assert.That(p.EmptyCache,Is.True);
            Assert.That(p.ColumnCount,Is.EqualTo(1));
            Assert.That(p.ColumnAt(0).Group.DeviceId,Is.Empty);Assert.That(p.ColumnAt(0).Channel,Is.Zero);
        }
        [Test] public void AncestorViewportIntersectionClipsOrRejectsCellScreenRects()
        {
            var viewport=new Rect(10,20,100,50);
            Assert.That(AudioRoutingMatrixView.Intersection(new Rect(0,0,30,40),viewport),Is.EqualTo(new Rect(10,20,20,20)));
            var clipped=AudioRoutingMatrixView.Intersection(new Rect(15,75,28,24),viewport);
            Assert.That(clipped.width,Is.Zero);Assert.That(clipped.height,Is.Zero);
            Assert.That(viewport.Contains(new Vector2(20,80)),Is.False);
        }
        [Test] public void WheelAtInternalLimitsDoesNotConsumeParentScroll()
        {
            var size=new Vector2(200,200);
            Assert.That(AudioRoutingMatrixView.WheelScroll(Vector2.zero,new Vector2(0,1),false,size,size),Is.EqualTo(Vector2.zero));
            Assert.That(AudioRoutingMatrixView.WheelScroll(Vector2.zero,new Vector2(0,-1),false,size,new Vector2(400,400)),Is.EqualTo(Vector2.zero));
            var atEnd=new Vector2(200,200);
            Assert.That(AudioRoutingMatrixView.WheelScroll(atEnd,new Vector2(0,1),false,size,new Vector2(400,400)),Is.EqualTo(atEnd));
            Assert.That(AudioRoutingMatrixView.WheelScroll(Vector2.zero,new Vector2(0,1),false,size,new Vector2(400,400)).y,Is.GreaterThan(0));
            Assert.That(AudioRoutingMatrixView.WheelScroll(Vector2.zero,new Vector2(0,1),true,size,new Vector2(400,400)).x,Is.GreaterThan(0));
        }
        [Test] public void CellGeometryAlignsRowsAndColumnsUnderTwoDimensionalScroll()
        {
            var offset=new Vector2(17,11);
            Rect a=AudioRoutingMatrixView.CellRect(3,5,offset);
            Rect right=AudioRoutingMatrixView.CellRect(3,6,offset),below=AudioRoutingMatrixView.CellRect(4,5,offset);
            Assert.That(a.x,Is.EqualTo(5*AudioRoutingMatrixView.CellWidth-17));
            Assert.That(a.y,Is.EqualTo(3*AudioRoutingMatrixView.RowHeight-11));
            Assert.That(a.xMax,Is.EqualTo(right.x));Assert.That(a.yMax,Is.EqualTo(below.y));
            Assert.That(a.y,Is.EqualTo(right.y));Assert.That(a.x,Is.EqualTo(below.x));
        }
        [Test] public void VisibleRangeIncludesPartialEdgesAndClampsAtEnd()
        {
            var r=AudioRoutingMatrixView.Visible(17,56,28,100);
            Assert.That(r.First,Is.Zero);Assert.That(r.End,Is.EqualTo(3));
            r=AudioRoutingMatrixView.Visible(28,56,28,100);
            Assert.That(r.First,Is.EqualTo(1));Assert.That(r.End,Is.EqualTo(3));
            r=AudioRoutingMatrixView.Visible(2789,56,28,100);
            Assert.That(r.First,Is.EqualTo(99));Assert.That(r.End,Is.EqualTo(100));
            Assert.That(AudioRoutingMatrixView.Visible(10000,50,28,100).Count,Is.Zero);
            Assert.That(AudioRoutingMatrixView.Visible(0,0,28,100).Count,Is.Zero);
            Assert.That(AudioRoutingMatrixView.Visible(0,50,28,0).Count,Is.Zero);
            Assert.That(AudioRoutingMatrixView.Visible(0,50,0,100).Count,Is.Zero);
        }
        [Test] public void LargeGridUsesCompactSpansAndViewportBoundedCellRanges()
        {
            var devices=Enumerable.Range(0,128).Select(i=>Device("device-"+i,4096)).ToArray();
            var p=AudioRoutingMatrixView.Capture(null,devices);
            Assert.That(p.ColumnCount,Is.EqualTo(1+128*4097));
            Assert.That(p.Groups.Length,Is.EqualTo(129));
            Assert.That(p.Groups.All(g=>g.StoredChannels.Length==0),Is.True);
            var last=p.ColumnAt(p.ColumnCount-1);
            Assert.That(last.Group.DeviceId,Is.EqualTo("device-127"));Assert.That(last.Channel,Is.EqualTo(4096));
            var columns=AudioRoutingMatrixView.Visible(450000*28+13,700,28,p.ColumnCount);
            var rows=AudioRoutingMatrixView.Visible(500*24+9,240,24,1000);
            Assert.That(columns.Count,Is.LessThanOrEqualTo(26));
            Assert.That(rows.Count,Is.LessThanOrEqualTo(11));
            Assert.That(columns.Count*rows.Count,Is.LessThanOrEqualTo(286));
            // Last channel of one interface and Auto 0 of the next never overlap.
            Assert.That(p.ColumnAt(4097).Channel,Is.EqualTo(4096));
            Assert.That(p.ColumnAt(4098).Channel,Is.Zero);
            Assert.That(p.ColumnAt(4098).Group.DeviceId,Is.EqualTo("device-1"));
        }
        static int CheckedCount(AudioRoutingMatrixView.Presentation p,AudioRoutingMatrixView.Row row)=>
            Enumerable.Range(0,p.ColumnCount).Select(p.ColumnAt).Count(c=>string.Equals(c.Group.DeviceId,row.DeviceId,StringComparison.Ordinal) && c.Channel==row.Channel);
    }
}
