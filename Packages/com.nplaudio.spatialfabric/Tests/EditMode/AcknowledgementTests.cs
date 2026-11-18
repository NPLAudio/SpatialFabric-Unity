// SILENT: synthetic observations, no-source failures, and invalid devices only. MIT.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using NPLAudio.SpatialFabric.Audio;
using NPLAudio.SpatialFabric.Osc;
namespace NPLAudio.SpatialFabric.Tests
{
    public sealed class AcknowledgementTests
    {
        Scene scene;
        GameObject root;
        SpatialFabricManager manager;
        PlaybackCue cue;
        LiveInputCue live;
        OscEndpoint monitor;
        PreparedAudioAsset asset;
        static int FreePort() {using(var socket=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))return ((IPEndPoint)socket.Client.LocalEndPoint).Port;}
        [SetUp] public void Setup()
        {
            SceneInventory.RegisterMainThread();scene=EditorSceneManager.NewPreviewScene();
            root=new GameObject("Silent acknowledgement test");SceneManager.MoveGameObjectToScene(root,scene);
            cue=root.AddComponent<PlaybackCue>();live=root.AddComponent<LiveInputCue>();manager=root.AddComponent<SpatialFabricManager>();
            cue.oscBase="/test/playback";
            monitor=new OscEndpoint();monitor.Start("127.0.0.1",FreePort());
            manager.enableInEditor=true;manager.monitorPort=monitor.Bound.Port;
            manager.spatialInputPort=FreePort();do {manager.playbackInputPort=FreePort();}while(manager.spatialInputPort==manager.playbackInputPort);
            Assert.That(manager.StartNetworking(),Is.True,manager.LastError);
        }
        [TearDown] public void Cleanup()
        {
            if(root!=null)UnityEngine.Object.DestroyImmediate(root);monitor?.Dispose();
            if(asset!=null)UnityEngine.Object.DestroyImmediate(asset);
            if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);
        }
        List<OscMessage> Collect(int count)
        {
            var messages=new List<OscMessage>();var deadline=DateTime.UtcNow.AddSeconds(3);
            while(messages.Count<count && DateTime.UtcNow<deadline) {messages.AddRange(monitor.Drain().Select(r=>r.Message));if(messages.Count<count)Thread.Sleep(5);}
            Assert.That(messages.Count,Is.GreaterThanOrEqualTo(count));return messages;
        }
        void Send(string address,bool spatial=false,params OscArg[] args)
        {
            using(var sender=new UdpClient())
            {
                byte[] packet=OscCodec.Encode(new OscMessage(address,args));long before=manager.Received;
                sender.Send(packet,packet.Length,new IPEndPoint(IPAddress.Loopback,spatial?manager.spatialInputPort:manager.playbackInputPort));
                var deadline=DateTime.UtcNow.AddSeconds(3);
                while(manager.Received==before && DateTime.UtcNow<deadline) {Thread.Sleep(5);manager.Tick(Time.realtimeSinceStartupAsDouble);}
                Assert.That(manager.Received,Is.EqualTo(before+1));
            }
        }
        static void Ack(OscMessage message,string command,string result,int playing)
        {
            Assert.That(message.Address,Does.EndWith("/ack"));Assert.That(message.Arguments.Select(a=>a.Tag),Is.EqualTo(new[]{'s','s','i','s'}));
            Assert.That(message.Arguments[0].Value,Is.EqualTo(command));Assert.That(message.Arguments[1].Value,Is.EqualTo(result));Assert.That(message.Arguments[2].Value,Is.EqualTo(playing));
        }
        [Test] public void NoSourceFailsAndBothPortsAnswerStatusWithoutPreparing()
        {
            Send(cue.oscBase+"/play");Ack(Collect(1).Single(),"play","failed",0);Assert.That(cue.IsPrepared,Is.False);
            foreach(bool spatial in new[]{false,true})
            {
                Send(cue.oscBase+"/status",spatial);var replies=Collect(2);Ack(replies[0],"status","received",0);
                Assert.That(replies[1].Address,Is.EqualTo(cue.oscBase+"/state"));Assert.That(replies[1].Arguments[0].Value,Is.EqualTo(0f));
            }
        }
        [Test] public void IgnoredRejectedAndStopRepliesDoNotStartAudio()
        {
            Send(cue.oscBase+"/play",false,OscArg.Float(.49f));Ack(Collect(1).Single(),"play","ignored",0);
            cue.loop=true;Send(cue.oscBase+"/loop",false,OscArg.String("off"));Ack(Collect(1).Single(),"loop","ignored",0);Assert.That(cue.loop,Is.True);
            Send(cue.oscBase+"/loop");Ack(Collect(1).Single(),"loop","received",0);Assert.That(cue.loop,Is.False);
            Send(live.oscBase+"/gain",false,OscArg.Float(9));Ack(Collect(1).Single(),"gain","rejected",0);Assert.That(live.gain,Is.EqualTo(1));
            Send(live.oscBase+"/gain",false,OscArg.Int(2));Ack(Collect(1).Single(),"gain","received",0);Assert.That(live.gain,Is.EqualTo(2));
            Send(live.oscBase+"/loop");Ack(Collect(1).Single(),"loop","rejected",0);
            long sent=manager.Sent;
            Send(cue.oscBase+"/state",false,OscArg.Float(1));
            Send(cue.oscBase+"/ack",false,OscArg.String("play"));
            Assert.That(manager.Sent,Is.EqualTo(sent));Assert.That(monitor.Drain(),Is.Empty);
            Send(cue.oscBase+"/"+new string('x',40));Ack(Collect(1).Single(),new string('x',32),"rejected",0);
            Send(cue.oscBase+"/stop");var replies=Collect(2);Ack(replies[0],"stop","received",0);Assert.That(replies[1].Arguments[0].Value,Is.EqualTo(0f));
            Assert.That(cue.IsPrepared,Is.False);Assert.That(live.IsLive,Is.False);
        }
        [Test] public void StatusReportsObservedStateNotIncomingState()
        {
            // Pure synthetic worker trace: no backend or PCM involved.
            var observation=(PlaybackObservation)typeof(PlaybackCue).GetField("observation",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cue);
            observation.Observe(AudioWorkerState.Playing,AudioEndReason.None,1,0,false,false,false,0,0);Collect(1);
            Send(cue.oscBase+"/status");var replies=Collect(2);Ack(replies[0],"status","received",1);Assert.That(replies[1].Arguments[0].Value,Is.EqualTo(1f));
            long sent=manager.Sent;Send(cue.oscBase+"/state",false,OscArg.Float(0));Assert.That(manager.Sent,Is.EqualTo(sent));Assert.That(cue.IsPlaying,Is.True);
        }
        [Test] public void FeedbackOnBothPortsAndReflectedMonitorNeverProducesReplies()
        {
            // The live base deliberately nests beneath playback; feedback must be filtered
            // before a parent cue can reject and acknowledge the remaining subpath.
            live.oscBase=cue.oscBase+"/nested";
            foreach(bool spatial in new[]{false,true})
            foreach(string address in new[]{cue.oscBase,live.oscBase})
            foreach(string command in new[]{"ack","state"})
            {
                long sent=manager.Sent;Send(address+"/"+command,spatial,OscArg.Float(1));
                Assert.That(manager.Sent,Is.EqualTo(sent));Assert.That(monitor.Drain(),Is.Empty);
            }
            manager.monitorPort=manager.playbackInputPort;
            long before=manager.Sent,received=manager.Received;Send(cue.oscBase+"/status");Assert.That(manager.Sent,Is.EqualTo(before+2));
            // The status ack/state return through the command listener and must terminate there.
            var deadline=DateTime.UtcNow.AddSeconds(3);
            while(manager.Received<received+3 && DateTime.UtcNow<deadline){Thread.Sleep(5);manager.Tick(Time.realtimeSinceStartupAsDouble);}
            Assert.That(manager.Received,Is.GreaterThanOrEqualTo(received+3));
            Assert.That(manager.Sent,Is.EqualTo(before+2));Assert.That(cue.IsPrepared,Is.False);Assert.That(live.IsLive,Is.False);
        }
        [Test] public void LocalStopRetiresPendingStartBeforeUnrelatedFailure()
        {
            foreach(var component in new Component[]{cue,live})
            {
                bool isLive=component is LiveInputCue;
                string field=isLive?"pendingLive":"pendingPlayback";
                var pending=(IDictionary)typeof(SpatialFabricManager).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(manager);
                uint version=(uint)component.GetType().GetProperty("AudioRequestVersion",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(component);
                pending.Add(component,version);
                if(isLive)live.Stop();else cue.Stop();
                // Simulate another local worker failing before the manager's next tick.
                component.GetType().GetField("<WorkerState>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(component,AudioWorkerState.Failed);
                manager.Tick(Time.realtimeSinceStartupAsDouble);Assert.That(pending.Count,Is.Zero);Assert.That(monitor.Drain(),Is.Empty);
            }
        }
        [Test] public void DisabledKnownCueStillAnswersStatusAndRejectsStartAsFailure()
        {
            cue.enabled=false;Send(cue.oscBase+"/play");Ack(Collect(1).Single(),"play","failed",0);
            Send(cue.oscBase+"/status");Ack(Collect(2)[0],"status","received",0);
        }
        [UnityTest] public IEnumerator UnpreparedPlayAndLiveStartAcknowledgeAsyncDeviceFailureOnce()
        {
            NativeAudio.Probe();NativeLive.Probe();
            if(!NativeAudio.IsAvailable || !NativeLive.IsAvailable)Assert.Ignore("Matching native plugin required for invalid-device regression");
            const string invalid="spatialfabric-intentionally-invalid-device-no-fallback";
            asset=ScriptableObject.CreateInstance<PreparedAudioAsset>();asset.SetPcm(new PcmData(new[]{.25f,-.25f},1,48000),"silent test source");
            cue.preparedAudio=asset;cue.configuredDeviceId=invalid;live.inputDeviceId=invalid;live.outputDeviceId=invalid;
            Assert.That(cue.IsPrepared,Is.False);
            foreach(bool isLive in new[]{false,true})
            {
                string cueBase=isLive?live.oscBase:cue.oscBase;
                Send(cueBase+"/play");Ack(Collect(1).Single(),"play","received",0);
                if(!isLive)Assert.That(cue.IsPrepared,Is.True,"OSC play must prepare on demand");
                double deadline=Time.realtimeSinceStartupAsDouble+5;
                while((isLive?live.WorkerState:cue.WorkerState)==AudioWorkerState.Starting && Time.realtimeSinceStartupAsDouble<deadline)
                {cue.Tick(Time.realtimeSinceStartupAsDouble);live.Tick(Time.realtimeSinceStartupAsDouble);yield return null;}
                Assert.That(isLive?live.WorkerState:cue.WorkerState,Is.EqualTo(AudioWorkerState.Failed));
                manager.Tick(Time.realtimeSinceStartupAsDouble);var failure=Collect(1).Single();Ack(failure,"play","failed",0);Assert.That((string)failure.Arguments[3].Value,Is.Not.Empty);
                manager.Tick(Time.realtimeSinceStartupAsDouble+1);yield return null;Assert.That(monitor.Drain(),Is.Empty);
            }
        }
    }
}
