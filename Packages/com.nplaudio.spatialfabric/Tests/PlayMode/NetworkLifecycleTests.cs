// Copyright (c) 2026 SpatialFabric Contributors. MIT.
using System.Collections;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using NPLAudio.SpatialFabric.Osc;
#if UNITY_EDITOR
using UnityEditor;
#endif
namespace NPLAudio.SpatialFabric.Tests
{
    public sealed class NetworkLifecycleTests
    {
        GameObject root;
        float oldScale;
        [SetUp] public void Setup() { oldScale = Time.timeScale; root = new GameObject("Runtime test"); }
        [TearDown] public void Cleanup() { Time.timeScale = oldScale; Object.DestroyImmediate(root); }
        static int FreePort() { using (var s = new UdpClient(new IPEndPoint(IPAddress.Loopback,0))) return ((IPEndPoint)s.Client.LocalEndPoint).Port; }
        SpatialFabricManager Manager()
        {
            var manager = root.AddComponent<SpatialFabricManager>(); manager.enableInPlay = true; manager.sendAdm = manager.sendCustom = false;
            manager.spatialInputPort = FreePort(); do { manager.playbackInputPort = FreePort(); } while (manager.playbackInputPort == manager.spatialInputPort);
            Assert.That(manager.StartNetworking(),Is.True,manager.LastError); return manager;
        }
        [UnityTest] public IEnumerator QueryDispatchContinuesAtTimeScaleZero()
        {
            var manager = Manager(); root.AddComponent<PlaybackCue>(); Time.timeScale = 0;
            using (var client = new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))
            {
                int replyPort = ((IPEndPoint)client.Client.LocalEndPoint).Port;
                byte[] p = OscCodec.Encode(new OscMessage("/spatialfabric/scene/query",OscArg.String("paused_test"),OscArg.Int(replyPort)));
                client.Send(p,p.Length,new IPEndPoint(IPAddress.Loopback,manager.playbackInputPort));
                float deadline = Time.realtimeSinceStartup+4;
                while (client.Available == 0 && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(client.Available,Is.GreaterThan(0));
                IPEndPoint peer = null; var message = OscCodec.Decode(client.Receive(ref peer))[0];
                Assert.That(message.Address,Is.EqualTo("/spatialfabric/scene/response")); Assert.That(message.Arguments[0].Value,Is.EqualTo("paused_test"));
                Assert.That(Time.timeScale,Is.Zero); Assert.That(root.GetComponent<PlaybackCue>().IsPlaying,Is.False);
            }
        }
        [UnityTest] public IEnumerator RuntimeClonesHaveUniqueReadOnlyKeysIncludingInactive()
        {
            var original=new GameObject("Clone source");original.transform.SetParent(root.transform);var source=original.AddComponent<PlaybackCue>();
            var clone=Object.Instantiate(original,root.transform);var inactive=Object.Instantiate(original,root.transform);inactive.SetActive(false);
            string a=source.Key,b=clone.GetComponent<PlaybackCue>().Key,c=inactive.GetComponent<PlaybackCue>().Key;
            Assert.That(a,Is.Not.EqualTo(b));Assert.That(c,Is.Not.EqualTo(b));Assert.That(c,Is.Not.EqualTo(a));yield return null;Assert.That(source.Key,Is.EqualTo(a));
        }
        [UnityTest] public IEnumerator DisableReleasesBothInputs()
        {
            var manager = Manager(); int a = manager.spatialInputPort,b = manager.playbackInputPort;
            manager.enabled = false; yield return null;
            Assert.That(manager.IsListening,Is.False);
            using (var first = new OscEndpoint()) using (var second = new OscEndpoint()) { first.Start("127.0.0.1",a); second.Start("127.0.0.1",b); }
        }
#if UNITY_EDITOR
        [UnityTest] public IEnumerator EditorPauseButtonStillDispatchesQueries()
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)Assert.Ignore("Editor Pause UI test requires graphics-enabled batch validation (--graphics)");
            var manager=Manager();bool finished=false,received=false,wasPaused=false;
            using(var client=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))
            {
                int port=((IPEndPoint)client.Client.LocalEndPoint).Port;
                byte[] data=OscCodec.Encode(new OscMessage("/spatialfabric/scene/query",OscArg.String("editor_pause"),OscArg.Int(port)));
                float deadline=Time.realtimeSinceStartup+5;
                EditorApplication.CallbackFunction observe=null;
                observe=()=>
                {
                    wasPaused|=EditorApplication.isPaused;
                    if(client.Available>0) {IPEndPoint peer=null;received=OscCodec.Decode(client.Receive(ref peer))[0].Address=="/spatialfabric/scene/response";}
                    if(received || Time.realtimeSinceStartup>=deadline) {EditorApplication.update-=observe;finished=true;EditorApplication.isPaused=false;}
                };
                try
                {
                    EditorApplication.isPaused=true;EditorApplication.update+=observe;
                    client.Send(data,data.Length,new IPEndPoint(IPAddress.Loopback,manager.spatialInputPort));
                    while(!finished)yield return null;
                    Assert.That(wasPaused,Is.True);Assert.That(received,Is.True);
                }
                finally {EditorApplication.update-=observe;EditorApplication.isPaused=false;}
            }
        }
#endif
        [Test] public void SecondBindFailureRollsBackFirstInput()
        {
            using (var collision = new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))
            {
                var manager = root.AddComponent<SpatialFabricManager>(); manager.enableInPlay = true; manager.spatialInputPort = FreePort(); manager.playbackInputPort = ((IPEndPoint)collision.Client.LocalEndPoint).Port;
                Assert.That(manager.StartNetworking(),Is.False); Assert.That(manager.IsListening,Is.False);
                using (var retry = new OscEndpoint()) retry.Start("127.0.0.1",manager.spatialInputPort);
            }
        }
    }
}
