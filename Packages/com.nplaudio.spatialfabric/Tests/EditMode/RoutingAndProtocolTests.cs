// Silent authoring/protocol integration: no PCM prepare, enumeration or playback. MIT.
using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using NPLAudio.SpatialFabric.Editor;
using NPLAudio.SpatialFabric.Protocols;
using NPLAudio.SpatialFabric.Routing;
namespace NPLAudio.SpatialFabric.Tests
{
    public sealed class RoutingAndProtocolTests
    {
        Scene scene;
        GameObject root;
        SpatialFabricManager manager;
        SpatialObject first,second;
        T Add<T>(string name)where T:Component {var g=new GameObject(name);g.transform.SetParent(root.transform);return g.AddComponent<T>();}
        [SetUp] public void Setup()
        {
            SceneInventory.RegisterMainThread();scene=EditorSceneManager.NewPreviewScene();root=new GameObject("Routing tests");SceneManager.MoveGameObjectToScene(root,scene);
            manager=Add<SpatialFabricManager>("Manager");manager.stage=Add<SpatialStage>("Stage");first=Add<SpatialObject>("Object, \"A\"\n日本語");second=Add<SpatialObject>("Object B");second.objectId=2;
            manager.objects.Add(first);manager.objects.Add(second);
        }
        [TearDown] public void Cleanup(){if(root!=null)UnityEngine.Object.DestroyImmediate(root);if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);}
        static void Set(System.Collections.Generic.List<string[]> rows,string column,int record,string value){int index=Array.IndexOf(rows[0],column);rows[record][index]=value;}
        static int FreePort(){using(var s=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))return ((IPEndPoint)s.Client.LocalEndPoint).Port;}
        [Test] public void SpatialCsvPreviewDoesNotMutateThenAppliesAsOneUndo()
        {
            first.customFields.Add(new CustomField{key="room",value="Hall,\n日本語"});var rows=RoutingCsvCodec.Parse(RoutingCsv.ExportSpatial(manager));
            string key=rows[1][Array.IndexOf(rows[0],"Key")];var target=manager.objects.Single(o=>o.Key==key);int old=target.objectId;
            Set(rows,"ObjectId",1,"99");var plan=RoutingCsv.PreviewSpatial(manager,RoutingCsvCodec.Write(rows));Assert.That(target.objectId,Is.EqualTo(old));Assert.That(plan.Count,Is.EqualTo(2));
            plan.Apply();Assert.That(target.objectId,Is.EqualTo(99));Assert.That(manager.IsListening,Is.False);
            Undo.PerformUndo();Assert.That(target.objectId,Is.EqualTo(old));Undo.PerformRedo();Assert.That(target.objectId,Is.EqualTo(99));
        }
        [Test] public void InvalidLastRowLeavesEveryRouteUntouched()
        {
            string beforeA=EditorJsonUtility.ToJson(first),beforeB=EditorJsonUtility.ToJson(second);
            var rows=RoutingCsvCodec.Parse(RoutingCsv.ExportSpatial(manager));Set(rows,"ObjectId",1,"99");Set(rows,"Width",2,"NaN");
            Assert.Throws<FormatException>(()=>RoutingCsv.PreviewSpatial(manager,RoutingCsvCodec.Write(rows)));
            Assert.That(EditorJsonUtility.ToJson(first),Is.EqualTo(beforeA));Assert.That(EditorJsonUtility.ToJson(second),Is.EqualTo(beforeB));
        }
        [Test] public void PreviewConcurrentEditRefusesBeforeApplyingAnyRows()
        {
            var rows=RoutingCsvCodec.Parse(RoutingCsv.ExportSpatial(manager));Set(rows,"ObjectId",1,"99");Set(rows,"ObjectId",2,"100");var plan=RoutingCsv.PreviewSpatial(manager,RoutingCsvCodec.Write(rows));
            second.width=.25f;Assert.Throws<InvalidOperationException>(()=>plan.Apply());Assert.That(first.objectId,Is.EqualTo(1));Assert.That(second.objectId,Is.EqualTo(2));Assert.That(second.width,Is.EqualTo(.25f));
        }
        [Test] public void UnknownOrDuplicateKeysNeverFallBackToLabels()
        {
            var rows=RoutingCsvCodec.Parse(RoutingCsv.ExportSpatial(manager));Set(rows,"Key",1,"unknown-key");Assert.Throws<FormatException>(()=>RoutingCsv.PreviewSpatial(manager,RoutingCsvCodec.Write(rows)));
            rows=RoutingCsvCodec.Parse(RoutingCsv.ExportSpatial(manager));Set(rows,"Key",2,rows[1][Array.IndexOf(rows[0],"Key")]);Assert.Throws<FormatException>(()=>RoutingCsv.PreviewSpatial(manager,RoutingCsvCodec.Write(rows)));
        }
        [Test] public void PlaybackCsvPreservesAudioSourceAndDoesNotPrepare()
        {
            var cue=Add<PlaybackCue>("Cue");var asset=ScriptableObject.CreateInstance<PreparedAudioAsset>();
            try
            {
                asset.SetPcm(new Audio.PcmData(new[]{.25f},1,48000),"reference only");cue.preparedAudio=asset;
                var rows=RoutingCsvCodec.Parse(RoutingCsv.ExportPlayback(manager));Set(rows,"DeviceId",1,"Configured, \"device\"\nID");Set(rows,"OutputChannel",1,"3");Set(rows,"OscBase",1,"/show/glass");Set(rows,"AutoReplay",1,"true");
                var plan=RoutingCsv.PreviewPlayback(manager,RoutingCsvCodec.Write(rows));Assert.That(cue.outputChannel,Is.Zero);plan.Apply();
                Assert.That(cue.outputChannel,Is.EqualTo(3));Assert.That(cue.oscBase,Is.EqualTo("/show/glass"));Assert.That(cue.configuredDeviceId,Does.Contain("\n"));Assert.That(cue.preparedAudio,Is.SameAs(asset));Assert.That(cue.IsPrepared,Is.False);Assert.That(cue.IsPlaying,Is.False);Assert.That(cue.ReplayPending,Is.False);
                Undo.PerformUndo();Assert.That(cue.outputChannel,Is.Zero);Assert.That(cue.preparedAudio,Is.SameAs(asset));
            }
            finally{UnityEngine.Object.DestroyImmediate(asset);}
        }
        [Test] public void PlaybackCsvRejectsBadBaseAndChannelBeforeMutation()
        {
            var cue=Add<PlaybackCue>("Cue");var rows=RoutingCsvCodec.Parse(RoutingCsv.ExportPlayback(manager));Set(rows,"OscBase",1,"/{unresolved}");Assert.Throws<FormatException>(()=>RoutingCsv.PreviewPlayback(manager,RoutingCsvCodec.Write(rows)));
            rows=RoutingCsvCodec.Parse(RoutingCsv.ExportPlayback(manager));Set(rows,"OutputChannel",1,"65");Assert.Throws<FormatException>(()=>RoutingCsv.PreviewPlayback(manager,RoutingCsvCodec.Write(rows)));Assert.That(cue.outputChannel,Is.Zero);
        }
        [Test] public void LiveNetworkingRefusesImportAndIsNeverStoppedImplicitly()
        {
            manager.enableInEditor=true;manager.spatialInputPort=FreePort();do{manager.playbackInputPort=FreePort();}while(manager.playbackInputPort==manager.spatialInputPort);
            Assert.That(manager.StartNetworking(),Is.True,manager.LastError);
            Assert.Throws<InvalidOperationException>(()=>RoutingCsv.PreviewSpatial(manager,RoutingCsv.ExportSpatial(manager)));Assert.That(manager.IsListening,Is.True);manager.StopNetworking();
        }
        [Test] public void CustomFieldsRoundTripAndReservedCollisionRejects()
        {
            first.customFields.Add(new CustomField{key="room",value="Hall,\"A\"\n日本語"});string csv=RoutingCsv.ExportSpatial(manager);var plan=RoutingCsv.PreviewSpatial(manager,csv);plan.Apply();Assert.That(first.customFields.Single().value,Is.EqualTo("Hall,\"A\"\n日本語"));
            var rows=RoutingCsvCodec.Parse(csv);Set(rows,"CustomFields",1,"{\"entries\":[{\"key\":\"id\",\"value\":\"9\"}]}");Assert.Throws<FormatException>(()=>RoutingCsv.PreviewSpatial(manager,RoutingCsvCodec.Write(rows)));
        }
        [Test] public void SceneQueryKeepsDictionaryFieldsAndReportsCustomConfiguration()
        {
            first.customFields.Add(new CustomField{key="room",value="Hall_A"});manager.customAddressTemplate="/{room}/{id}/{axis}";manager.customProtocol.coordinates=CustomCoordinates.AED;manager.customProtocol.sendMode=CustomSendMode.Discrete;manager.customProtocol.argumentTemplate="a e d";manager.customProtocol.sendRateHz=12;
            string before=EditorJsonUtility.ToJson(manager);string json=SceneInventory.GetJson(manager);
            Assert.That(json,Does.Contain("\"custom_fields\":{\"room\":\"Hall_A\"}"));Assert.That(json,Does.Contain("\"custom_coordinate_mode\":\"AED\""));Assert.That(json,Does.Contain("\"custom_send_mode\":\"Discrete\""));Assert.That(json,Does.Contain("\"send_rate_hz\":12"));Assert.That(EditorJsonUtility.ToJson(manager),Is.EqualTo(before));Assert.That(manager.IsListening,Is.False);
        }
        [Test] public void NonfiniteSnapshotFailsAsBoundedSchemaError()
        {
            first.gain=float.NaN;Assert.That(SceneInventory.GetJson(manager),Does.Contain("snapshot_encoding_or_page_limit_reduce_page_size"));
        }
        [TestCase(RadarView.Top,.5f,1)] [TestCase(RadarView.Front,.5f,.25f)] [TestCase(RadarView.Side,1,.25f)]
        public void RadarUsesCanonicalAxes(RadarView view,float x,float y){Assert.That(SpatialRadar.Project(new Vector3(1,.5f,.25f),view),Is.EqualTo(new Vector2(x,y)));}
        static System.Collections.Generic.List<Osc.ReceivedOsc> WaitPackets(Osc.OscEndpoint endpoint,int count)
        {
            var rows=new System.Collections.Generic.List<Osc.ReceivedOsc>();var deadline=DateTime.UtcNow.AddSeconds(2);
            while(rows.Count<count && DateTime.UtcNow<deadline){rows.AddRange(endpoint.Drain());if(rows.Count<count)System.Threading.Thread.Sleep(2);}
            Assert.That(rows.Count,Is.EqualTo(count));return rows;
        }
        void StartForWire(Osc.OscEndpoint adm,Osc.OscEndpoint custom)
        {
            adm.Start("127.0.0.1",FreePort());custom.Start("127.0.0.1",FreePort());manager.admPort=adm.Bound.Port;manager.customPort=custom.Bound.Port;
            manager.enableInEditor=true;manager.spatialInputPort=FreePort();do{manager.playbackInputPort=FreePort();}while(manager.playbackInputPort==manager.spatialInputPort);
            manager.admCoordinateMode=AdmCoordinateMode.AED;manager.customProtocol.coordinates=CustomCoordinates.AED;manager.customProtocol.sendMode=CustomSendMode.Discrete;manager.customProtocol.argumentTemplate="a e d gain width id";
            manager.customAddressTemplate="/fixture/{room}/{id}/{axis}";foreach(var obj in manager.objects)obj.customFields.Add(new CustomField{key="room",value="UnityHall"});
            manager.stage.dimensionsMeters=Vector3.one*2;first.transform.position=Vector3.forward;second.transform.position=Vector3.right;
            Assert.That(manager.StartNetworking(),Is.True,manager.LastError);
        }
        [Test] public void RealWireDiscreteAedChangeOnlyAndInvalidFrameRecovery()
        {
            using(var adm=new Osc.OscEndpoint())using(var custom=new Osc.OscEndpoint())
            {
                StartForWire(adm,custom);manager.admSendOnlyOnChange=true;manager.customProtocol.sendOnlyOnChange=true;manager.Tick(10);
                var a=WaitPackets(adm,10);var c=WaitPackets(custom,12);Assert.That(a.Any(m=>m.Message.Address.EndsWith("/xyz")),Is.False);
                Assert.That((float)c.Single(m=>m.Message.Address=="/fixture/UnityHall/1/distance").Message.Arguments[0].Value,Is.EqualTo(1));
                Assert.That((float)c.Single(m=>m.Message.Address=="/fixture/UnityHall/2/azimuth").Message.Arguments[0].Value,Is.EqualTo(-.5f));
                manager.Tick(10.2);Assert.That(manager.Sent,Is.EqualTo(22));
                first.gain=.5f;manager.Tick(10.4);WaitPackets(adm,10);WaitPackets(custom,12);
                manager.customAddressTemplate="/broken/{missing}/{id}";first.gain=.25f;manager.Tick(10.6);WaitPackets(adm,10);System.Threading.Thread.Sleep(25);Assert.That(custom.Drain().Count,Is.Zero);Assert.That(manager.LastError,Does.Contain("Unresolved"));
                manager.customAddressTemplate="/fixture/Recovered/{id}/{axis}";manager.Tick(10.8);var recovered=WaitPackets(custom,12);Assert.That(recovered.All(m=>m.Message.Address.StartsWith("/fixture/Recovered/")),Is.True);Assert.That(adm.Drain().Count,Is.Zero);Assert.That(manager.Sent,Is.EqualTo(66));
            }
        }
        [Test] public void ProtocolRateOverridesAreIndependentOnRealWire()
        {
            using(var adm=new Osc.OscEndpoint())using(var custom=new Osc.OscEndpoint())
            {
                StartForWire(adm,custom);manager.admSendRateHz=10;manager.customProtocol.sendRateHz=4;manager.Tick(10);WaitPackets(adm,10);WaitPackets(custom,12);
                manager.Tick(10.11);WaitPackets(adm,10);System.Threading.Thread.Sleep(25);Assert.That(custom.Drain().Count,Is.Zero);
                manager.Tick(10.26);WaitPackets(adm,10);WaitPackets(custom,12);Assert.That(manager.Sent,Is.EqualTo(54));
            }
        }
        [UnityTest] public IEnumerator WindowRadarRepaintNeverStartsNetworkingOrPreparesAudio()
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)Assert.Ignore("Radar UI repaint test requires --graphics");
            var cue=Add<PlaybackCue>("Cue");var window=EditorWindow.GetWindow<SpatialFabricWindow>();
            typeof(SpatialFabricWindow).GetField("manager",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(window,manager);
            try{window.Show();window.Repaint();yield return null;yield return null;Assert.That(manager.IsListening,Is.False);Assert.That(cue.IsPrepared,Is.False);Assert.That(cue.IsPlaying,Is.False);}
            finally{window.Close();}
        }
        [Test] public void AdmCoordinateSelectionDoesNotChangeMetadataOrCoherence()
        {
            var xyz=SpatialMessages.Adm(first,Vector3.one,AdmCoordinateMode.XYZ).ToArray();var aed=SpatialMessages.Adm(first,Vector3.one,AdmCoordinateMode.AED).ToArray();
            Assert.That(xyz.Any(m=>m.Address.EndsWith("/xyz")),Is.True);Assert.That(xyz.Any(m=>m.Address.EndsWith("/aed")),Is.False);Assert.That(aed.Any(m=>m.Address.EndsWith("/xyz")),Is.False);Assert.That(aed.Any(m=>m.Address.EndsWith("/gain")),Is.True);
        }
    }
}
