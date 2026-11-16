// Copyright (c) 2026 SpatialFabric Contributors. MIT.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using NPLAudio.SpatialFabric.Osc;
namespace NPLAudio.SpatialFabric.Tests
{
    public sealed class SpatialFabricTests
    {
        GameObject root;
        Scene scene;
        T Component<T>(string name) where T : Component { var g = new GameObject(name); g.transform.SetParent(root.transform); return g.AddComponent<T>(); }
        [SetUp] public void Setup()
        {
            SceneInventory.RegisterMainThread();
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Test root"); SceneManager.MoveGameObjectToScene(root,scene);
        }
        [TearDown] public void Cleanup() { if (root != null) UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
        [TestCase(0,0,1,1,0,0)] [TestCase(1,0,0,0,1,0)] [TestCase(0,1,0,0,0,1)]
        public void CanonicalAxes(float x,float y,float z,float f,float r,float u)
        {
            var stage = Component<SpatialStage>("Stage"); stage.dimensionsMeters = Vector3.one*2;
            Assert.That(stage.Normalize(new Vector3(x,y,z)),Is.EqualTo(new Vector3(f,r,u)));
        }
        [Test] public void StageRotationDimensionsAndFlip()
        {
            var stage = Component<SpatialStage>("Stage"); stage.dimensionsMeters = new Vector3(20,10,4);
            stage.transform.SetPositionAndRotation(new Vector3(2,3,4),Quaternion.Euler(0,90,0));
            Vector3 point = stage.transform.TransformPoint(new Vector3(2.5f,1,5));
            Assert.That(Vector3.Distance(stage.Normalize(point),new Vector3(.5f,.5f,.5f)),Is.LessThan(.0001f));
            stage.flipRight = true; Assert.That(stage.Normalize(point).y,Is.EqualTo(-.5f).Within(.0001f));
        }
        [Test] public void FollowIsVirtualAndUsesAssignedRoot()
        {
            var stage = Component<SpatialStage>("Stage"); var listener = Component<SpatialObject>("Listener");
            stage.dimensionsMeters = Vector3.one*2; stage.listener = listener.transform; stage.followPositionAndOrientation = true;
            listener.transform.position = new Vector3(10,0,0);
            Assert.That(stage.Normalize(new Vector3(10,0,1)),Is.EqualTo(Vector3.right));
            Assert.That(stage.transform.position,Is.EqualTo(Vector3.zero));
        }
        [Test] public void SphereLimitedXyzAndAedAgree()
        {
            var obj = Component<SpatialObject>("Object"); var messages = SpatialMessages.Adm(obj,Vector3.one).ToArray();
            var xyz = messages[0].Arguments; var aed = messages[1].Arguments;
            float radius = Mathf.Sqrt((float)xyz[0].Value*(float)xyz[0].Value+(float)xyz[1].Value*(float)xyz[1].Value+(float)xyz[2].Value*(float)xyz[2].Value);
            Assert.That(radius,Is.EqualTo(1).Within(.0001f)); Assert.That((float)aed[2].Value,Is.EqualTo(radius).Within(.0001f)); Assert.That((float)aed[0].Value,Is.EqualTo(-45).Within(.0001f));
        }
        [Test] public void CustomOverrideAndUnresolvedTemplate()
        {
            var obj = Component<SpatialObject>("Object"); obj.objectId = 5; obj.customObjectIdOverride = 9;
            Assert.That(SpatialMessages.Custom(obj,Vector3.zero,"/obj/{id}/xyz").Address,Is.EqualTo("/obj/9/xyz"));
            Assert.Throws<FormatException>(() => SpatialMessages.Custom(obj,Vector3.zero,"/{unknown}/xyz"));
        }
        [Test] public void QueryIsReadOnlyAndNoSourceCannotPlay()
        {
            var manager = Component<SpatialFabricManager>("Manager"); var cue = Component<PlaybackCue>("Cue");
            string before = EditorJsonUtility.ToJson(cue); string json = SceneInventory.GetJson(manager);
            var page = JsonUtility.FromJson<ScenePage>(json);
            Assert.That(page.schema_version,Is.EqualTo(1)); Assert.That(page.total,Is.EqualTo(2));
            Assert.That(page.objects.Single(r => r.kind == "playback").backend_available,Is.EqualTo(cue.BackendAvailable));
            Assert.That(cue.IsPrepared,Is.False);
            Assert.That(manager.IsListening,Is.False); Assert.That(cue.IsPlaying,Is.False); Assert.That(EditorJsonUtility.ToJson(cue),Is.EqualTo(before));
            Assert.That(cue.TryPlay(),Is.False); Assert.That(cue.LastError,Does.Contain("no audio was started"));
        }
        [Test] public void DuplicateIdentitiesFailReadOnlyThenRepairExplicitly()
        {
            var manager = Component<SpatialFabricManager>("Manager"); var a = Component<PlaybackCue>("A"); var b = Component<PlaybackCue>("B");
            var source = new SerializedObject(a); var target = new SerializedObject(b);
            target.FindProperty("persistentId").stringValue = source.FindProperty("persistentId").stringValue; target.ApplyModifiedPropertiesWithoutUndo();
            string before = b.Key; Assert.That(SceneInventory.GetJson(manager),Does.Contain("duplicate_identity_repair_required")); Assert.That(b.Key,Is.EqualTo(before));
            Assert.That(NPLAudio.SpatialFabric.Editor.SceneIdentityTools.RepairScene(scene),Is.EqualTo(1));
            Assert.That(SceneInventory.GetJson(manager),Does.Not.Contain("duplicate_identity_repair_required"));
        }
        [Test] public void MainThreadGuardPrecedesUnityAccess()
        {
            var manager = Component<SpatialFabricManager>("Manager");
            Assert.That(Task.Run(() => SceneInventory.GetJson(manager)).Result,Does.Contain("game_thread_required"));
        }
        [Test] public void PagingIsStableAndSceneScoped()
        {
            var manager = Component<SpatialFabricManager>("Manager"); var stage = Component<SpatialStage>("Stage"); manager.stage = stage;
            for (int i = 0; i < 5; ++i) manager.objects.Add(Component<SpatialObject>("Object "+i));
            var a = JsonUtility.FromJson<ScenePage>(SceneInventory.GetJson(manager,0,3));
            var b = JsonUtility.FromJson<ScenePage>(SceneInventory.GetJson(manager,3,3));
            var c = JsonUtility.FromJson<ScenePage>(SceneInventory.GetJson(manager,6,3));
            Assert.That(a.total,Is.EqualTo(7)); Assert.That(a.next_offset,Is.EqualTo(3)); Assert.That(c.returned,Is.EqualTo(1)); Assert.That(c.next_offset,Is.EqualTo(-1));
            Assert.That(a.objects.Concat(b.objects).Concat(c.objects).Select(r => r.key).Distinct().Count(),Is.EqualTo(7));
            Assert.That(SceneInventory.GetJson(manager,0,65),Does.Contain("invalid_page"));
        }
        [Test] public void WorldTransformPreservesV1CanonicalCentimeters()
        {
            var manager = Component<SpatialFabricManager>("Manager"); manager.transform.position = new Vector3(1,2,3);
            var page = JsonUtility.FromJson<ScenePage>(SceneInventory.GetJson(manager));
            Assert.That(page.objects.Single().world_transform.position_cm,Is.EqualTo(new[] {300f,100f,200f}));
        }
        [Test] public void Utf8ChunksNeverSplitSurrogatesOrExceedCap()
        {
            string text = string.Concat(Enumerable.Repeat("🎧日本語",1000)); var chunks = SceneQueryResponder.Chunks(text);
            Assert.That(string.Concat(chunks),Is.EqualTo(text));
            foreach (string part in chunks) { Assert.That(Encoding.UTF8.GetByteCount(part),Is.LessThanOrEqualTo(900)); Assert.That(char.IsLowSurrogate(part[0]),Is.False); Assert.That(char.IsHighSurrogate(part[part.Length-1]),Is.False); }
        }
        [Serializable] sealed class VectorFixture { public VectorRow[] vectors; }
        [Serializable] sealed class VectorRow { public string name; public float[] canonical,unity_m,adm_xyz,adm_aed; }
        [Serializable] sealed class WireFixture { public WireRow[] valid,invalid; }
        [Serializable] sealed class WireRow { public string name,address,hex; }
        static string Fixture(string name)
        {
            string rootPath = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SpatialStage).Assembly).resolvedPath;
            return File.ReadAllText(Path.Combine(rootPath,"Tests/EditMode/Fixtures",name));
        }
        static byte[] Hex(string s) => Enumerable.Range(0,s.Length/2).Select(i => Convert.ToByte(s.Substring(i*2,2),16)).ToArray();
        [Test] public void SharedWireFixturesMatchActualCodec()
        {
            var fixture = JsonUtility.FromJson<WireFixture>(Fixture("osc-v1.json"));
            foreach (var row in fixture.valid)
            {
                byte[] p = Hex(row.hex); var m = OscCodec.Decode(p); Assert.That(m.Single().Address,Is.EqualTo(row.address),row.name); Assert.That(OscCodec.Encode(m.Single()),Is.EqualTo(p),row.name);
            }
            foreach (var row in fixture.invalid) Assert.Throws<FormatException>(() => OscCodec.Decode(Hex(row.hex)),row.name);
        }
        [Test] public void SharedCoordinateFixturesMatchStageAndAdm()
        {
            var fixture = JsonUtility.FromJson<VectorFixture>(Fixture("coordinates-v1.json")); var stage = Component<SpatialStage>("Stage"); stage.dimensionsMeters = Vector3.one*2;
            var obj = Component<SpatialObject>("Object");
            foreach (var row in fixture.vectors)
            {
                Vector3 p = stage.Normalize(new Vector3(row.unity_m[0],row.unity_m[1],row.unity_m[2]));
                Assert.That(SceneInventory.Vec(p),Is.EqualTo(row.canonical));
                var messages = SpatialMessages.Adm(obj,p).Take(2).ToArray();
                Assert.That(messages[0].Arguments.Select(a => (float)a.Value),Is.EqualTo(row.adm_xyz));
                for (int i = 0; i < 3; ++i) Assert.That((float)messages[1].Arguments[i].Value,Is.EqualTo(row.adm_aed[i]).Within(.001f));
            }
        }
        [Test] public void NewManagerNeverStartsImplicitly()
        {
            var manager = Component<SpatialFabricManager>("Manager"); Assert.That(manager.IsListening,Is.False); Assert.That(manager.StartNetworking(),Is.False); Assert.That(manager.BoundSpatialPort,Is.Zero);
        }
    }
}
