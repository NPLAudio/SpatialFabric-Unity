// Silent completion gates: multiple targets, source CSV, authoring and prefab/scene lifecycle. MIT.
using System;
using System.Collections;
using System.IO;
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
    public sealed class CompletionTests
    {
        Scene scene;GameObject root;SpatialFabricManager manager;string folder;
        T Add<T>(string name)where T:Component{var g=new GameObject(name);g.transform.SetParent(root.transform);return g.AddComponent<T>();}
        [SetUp] public void Setup()
        {
            SceneInventory.RegisterMainThread();scene=EditorSceneManager.NewPreviewScene();root=new GameObject("Completion tests");SceneManager.MoveGameObjectToScene(root,scene);manager=Add<SpatialFabricManager>("Manager");manager.stage=Add<SpatialStage>("Stage");manager.stage.dimensionsMeters=Vector3.one*2;
            folder="Assets/__SpatialFabricTests_"+Guid.NewGuid().ToString("N");AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
        }
        [TearDown] public void Cleanup(){if(root!=null)UnityEngine.Object.DestroyImmediate(root);if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);AssetDatabase.DeleteAsset(folder);}
        static int Port(){using(var s=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))return ((IPEndPoint)s.Client.LocalEndPoint).Port;}
        PreparedAudioAsset Asset(string name)
        {
            var value=ScriptableObject.CreateInstance<PreparedAudioAsset>();value.SetPcm(new Audio.PcmData(new[]{.25f},1,48000),name);AssetDatabase.CreateAsset(value,folder+"/"+name+".asset");return value;
        }
        [Test] public void LoopWireUsesAudioLoomThresholdAndAddressOnlyOff()
        {
            var cue=Add<PlaybackCue>("Cue");cue.oscBase="/completion/cue";cue.loop=true;
            manager.enableInEditor=true;manager.spatialInputPort=Port();do{manager.playbackInputPort=Port();}while(manager.playbackInputPort==manager.spatialInputPort);Assert.That(manager.StartNetworking(),Is.True);
            using(var sender=new UdpClient())
            {
                void Send(params Osc.OscArg[] args){long received=manager.Received;byte[] packet=Osc.OscCodec.Encode(new Osc.OscMessage(cue.oscBase+"/loop",args));sender.Send(packet,packet.Length,new IPEndPoint(IPAddress.Loopback,manager.playbackInputPort));var deadline=DateTime.UtcNow.AddSeconds(2);while(manager.Received==received && DateTime.UtcNow<deadline){System.Threading.Thread.Sleep(2);manager.Tick(Time.realtimeSinceStartupAsDouble);}Assert.That(manager.Received,Is.GreaterThan(received));}
                Send(Osc.OscArg.Float(.5f));Assert.That(cue.loop,Is.False);Send(Osc.OscArg.Int(0),Osc.OscArg.Float(1));Assert.That(cue.loop,Is.True);Send();Assert.That(cue.loop,Is.False);Assert.That(cue.IsPrepared||cue.IsPlaying,Is.False);
            }
        }
        [Test] public void PositionOnlyFollowKeepsAuthoredOrientation()
        {
            var listener=Add<SpatialObject>("Listener");listener.transform.SetPositionAndRotation(new Vector3(10,0,0),Quaternion.Euler(0,90,0));manager.stage.listener=listener.transform;manager.stage.followMode=ListenerFollowMode.PositionOnly;
            Assert.That(Vector3.Distance(manager.stage.Normalize(new Vector3(10,0,1)),Vector3.right),Is.LessThan(.001f));
            manager.stage.followMode=ListenerFollowMode.PositionAndOrientation;Assert.That(manager.stage.Normalize(new Vector3(10,0,1)).y,Is.EqualTo(-1).Within(.001));Assert.That(manager.stage.transform.position,Is.EqualTo(Vector3.zero));
        }
        [Test] public void BoundTransformAndIndependentDestinationsUsePerTargetIds()
        {
            var binding=Add<SpatialObject>("Binding");var pose=Add<SpatialObject>("Pose");pose.transform.position=Vector3.forward;binding.boundTransform=pose.transform;binding.displayLabel="Resolved label";binding.admObjectIdOverride=7;manager.objects.Add(binding);
            using(var a=new Osc.OscEndpoint())using(var b=new Osc.OscEndpoint())using(var c=new Osc.OscEndpoint())
            {
                foreach(var receiver in new[]{a,b,c})receiver.Start("127.0.0.1",Port());
                manager.outputs.Add(new SpatialOutput{key="adm-a",protocol=SpatialProtocol.ADM,port=a.Bound.Port});manager.outputs.Add(new SpatialOutput{key="adm-b",protocol=SpatialProtocol.ADM,port=b.Bound.Port});
                manager.outputs.Add(new SpatialOutput{key="custom",protocol=SpatialProtocol.Custom,port=c.Bound.Port,addressTemplate="/{room}/{id}/xyz"});
                binding.targetOverrides.Add(new SpatialTargetOverride{outputKey="adm-b",objectIdOverride=12});binding.targetOverrides.Add(new SpatialTargetOverride{outputKey="custom",objectIdOverride=99,fields=new System.Collections.Generic.List<CustomField>{new CustomField{key="room",value="RoomA"}}});
                manager.enableInEditor=true;manager.spatialInputPort=Port();do{manager.playbackInputPort=Port();}while(manager.playbackInputPort==manager.spatialInputPort);Assert.That(manager.StartNetworking(),Is.True);manager.Tick(10);
                System.Threading.Thread.Sleep(100);var aa=a.Drain();var bb=b.Drain();var cc=c.Drain();Assert.That(aa.Count,Is.EqualTo(6));Assert.That(bb.Count,Is.EqualTo(6));Assert.That(cc.Single().Message.Address,Is.EqualTo("/RoomA/99/xyz"));Assert.That(aa[0].Message.Address,Is.EqualTo("/adm/obj/7/xyz"));Assert.That(bb[0].Message.Address,Is.EqualTo("/adm/obj/12/xyz"));
                Assert.That((float)aa[0].Message.Arguments[1].Value,Is.EqualTo(1));Assert.That(aa.Last().Message.Arguments[0].Value,Is.EqualTo("Resolved label"));
                string json=SceneInventory.GetJson(manager);Assert.That(json,Does.Contain("\"output_key\":\"adm-b\""));Assert.That(json,Does.Contain("\"object_id\":99"));
            }
        }
        [Test] public void BulkBindingsAndIdsHaveOneUndoAndNoStartupIo()
        {
            var a=Add<PlaybackCue>("Actor A");var b=Add<PlaybackCue>("Actor B");Assert.That(SpatialAuthoring.AddBindings(manager,new[]{a.gameObject,b.gameObject}),Is.EqualTo(2));var targets=manager.objects.ToArray();
            SpatialAuthoring.EditBindings(manager,targets,40,true);Assert.That(targets.Select(o=>o.objectId),Is.EqualTo(new[]{40,41}));Assert.That(targets.All(o=>o.mute),Is.True);Undo.PerformUndo();Assert.That(targets.Select(o=>o.objectId),Is.EqualTo(new[]{1,2}));Assert.That(manager.IsListening,Is.False);Assert.That(a.IsPrepared||b.IsPrepared,Is.False);
        }
        [Test] public void PlaybackV2AssignsExactSourceReferencesWithUndo()
        {
            var cue=Add<PlaybackCue>("Cue");var a=Asset("A");var b=Asset("B");cue.preparedAudio=a;
            string reference;AssetDatabase.TryGetGUIDAndLocalFileIdentifier(b,out string guid,out long id);reference=guid+":"+id;
            var rows=RoutingCsvCodec.Parse(PlaybackAssetCsv.Export(manager));rows[1][Array.IndexOf(rows[0],"PreparedAudioRef")]=reference;rows[1][Array.IndexOf(rows[0],"OutputChannel")]="2";
            var plan=PlaybackAssetCsv.Preview(manager,RoutingCsvCodec.Write(rows));Assert.That(cue.preparedAudio,Is.SameAs(a));plan.Apply();Assert.That(cue.preparedAudio,Is.SameAs(b));Assert.That(cue.outputChannel,Is.EqualTo(2));Assert.That(cue.IsPrepared,Is.False);
            Undo.PerformUndo();Assert.That(cue.preparedAudio,Is.SameAs(a));Assert.That(cue.outputChannel,Is.Zero);
        }
        [Test] public void ChangedSourceSincePreviewIsRefusedWithoutRouteMutation()
        {
            var cue=Add<PlaybackCue>("Cue");var a=Asset("A");cue.preparedAudio=a;var rows=RoutingCsvCodec.Parse(PlaybackAssetCsv.Export(manager));rows[1][Array.IndexOf(rows[0],"OutputChannel")]="5";var plan=PlaybackAssetCsv.Preview(manager,RoutingCsvCodec.Write(rows));
            a.SetPcm(new Audio.PcmData(new[]{.75f},1,48000),"new");EditorUtility.SetDirty(a);AssetDatabase.SaveAssets();Assert.Throws<InvalidOperationException>(()=>plan.Apply());Assert.That(cue.outputChannel,Is.Zero);
        }
        [Test] public void WrongAssetTypeInV2FailsBeforeAnySceneChange()
        {
            var cue=Add<PlaybackCue>("Cue");cue.preparedAudio=Asset("A");var rows=RoutingCsvCodec.Parse(PlaybackAssetCsv.Export(manager));rows[1][Array.IndexOf(rows[0],"ClipRef")]=rows[1][Array.IndexOf(rows[0],"PreparedAudioRef")];rows[1][Array.IndexOf(rows[0],"OutputChannel")]="5";
            var plan=PlaybackAssetCsv.Preview(manager,RoutingCsvCodec.Write(rows));Assert.Throws<FormatException>(()=>plan.Apply());Assert.That(cue.outputChannel,Is.Zero);Assert.That(cue.clip,Is.Null);
        }
        [Test] public void PrefabDuplicateRepairAndBulkOverridesPersist()
        {
            var source=Add<SpatialObject>("Prefab object");string path=folder+"/Object.prefab";var prefab=PrefabUtility.SaveAsPrefabAsset(source.gameObject,path);UnityEngine.Object.DestroyImmediate(source.gameObject);
            var first=((GameObject)PrefabUtility.InstantiatePrefab(prefab,scene)).GetComponent<SpatialObject>();var second=((GameObject)PrefabUtility.InstantiatePrefab(prefab,scene)).GetComponent<SpatialObject>();first.transform.SetParent(root.transform);second.transform.SetParent(root.transform);manager.objects.Add(first);manager.objects.Add(second);
            Assert.That(first.Key,Is.EqualTo(second.Key));Assert.Throws<FormatException>(()=>RoutingCsv.ExportSpatial(manager));Assert.That(SceneIdentityTools.RepairScene(scene),Is.EqualTo(1));Assert.That(first.Key,Is.Not.EqualTo(second.Key));
            SpatialAuthoring.EditBindings(manager,new[]{first},75);Assert.That(PrefabUtility.GetPropertyModifications(first).Any(p=>p.propertyPath=="objectId" && p.value=="75"),Is.True);Undo.PerformUndo();Assert.That(first.objectId,Is.EqualTo(1));
        }
        [Test] public void PrefabAssetIdentityAndBoundReferenceSurviveImport()
        {
            var binding=Add<SpatialObject>("Persisted binding");var target=new GameObject("Target");target.transform.SetParent(binding.transform);binding.boundTransform=target.transform;binding.objectId=51;
            string path=folder+"/Persisted.prefab";var prefab=PrefabUtility.SaveAsPrefabAsset(binding.gameObject,path);string key=prefab.GetComponent<SpatialObject>().Key;
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);var restored=AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<SpatialObject>();
            Assert.That(restored.Key,Is.EqualTo(key));Assert.That(restored.objectId,Is.EqualTo(51));Assert.That(restored.Pose.name,Is.EqualTo("Target"));
        }
        [Test] public void IsometricProjectionAndDestinationIdValidation()
        {
            Assert.That(SpatialRadar.Project(new Vector3(1,-1,1),RadarView.Isometric),Is.EqualTo(new Vector2(-1,.5f)));
            var obj=Add<SpatialObject>("Binding");manager.objects.Add(obj);manager.outputs.Add(new SpatialOutput{key="one"});manager.outputs.Add(new SpatialOutput{key="one"});Assert.That(SpatialAuthoring.Validate(manager),Does.Contain("Missing/duplicate output key"));
        }
        [UnityTest] public IEnumerator EveryOperatorPanelRendersWithoutStartupIo()
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)Assert.Ignore("Panel repaint requires graphics");
            Add<PlaybackCue>("Cue");var window=EditorWindow.GetWindow<SpatialFabricWindow>();var type=typeof(SpatialFabricWindow);type.GetField("manager",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(window,manager);
            try{for(int i=0;i<6;++i){type.GetField("tab",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(window,i);window.Repaint();yield return null;yield return null;}Assert.That(manager.IsListening,Is.False);Assert.That(SceneInventory.InScene<PlaybackCue>(scene).All(c=>!c.IsPrepared && !c.IsPlaying),Is.True);}
            finally{window.Close();}
        }
    }
}
