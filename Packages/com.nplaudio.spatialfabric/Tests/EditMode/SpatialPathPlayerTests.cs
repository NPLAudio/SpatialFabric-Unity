// Copyright (c) 2026 SpatialFabric Contributors. MIT.
using System.Text;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using NPLAudio.SpatialFabric.Editor;
namespace NPLAudio.SpatialFabric.Tests
{
    public sealed class SpatialPathPlayerTests
    {
        const string Header = "{\"schema\":\"org.nplaudio.spatialfabric.path\",\"schema_version\":1,\"space\":\"canonical\",";
        const string Line = Header + "\"tracks\":[{\"id\":\"a\",\"target\":{\"kind\":\"object\",\"key\":\"k\"},\"keyframes\":[{\"t\":0,\"position\":[0,0,0]},{\"t\":2,\"position\":[1,1,1]}],\"parameters\":{\"gain\":[{\"t\":0,\"value\":0},{\"t\":2,\"value\":2}]}},"
            + "{\"id\":\"b\",\"target\":{\"kind\":\"object\",\"key\":\"k2\"},\"keyframes\":[{\"t\":0,\"position\":[0,0,0]},{\"t\":1,\"position\":[0,1,0]}]}]}";
        GameObject root;
        Scene scene;
        System.Collections.Generic.List<Object> created = new System.Collections.Generic.List<Object>();

        [SetUp] public void Setup()
        {
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Path test root"); SceneManager.MoveGameObjectToScene(root, scene);
        }
        [TearDown] public void Cleanup()
        {
            if (root != null) Object.DestroyImmediate(root);
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
            EditorSceneManager.ClosePreviewScene(scene);
        }

        SpatialPathAsset Asset(string json) { var a = ScriptableObject.CreateInstance<SpatialPathAsset>(); a.SetJson(json); created.Add(a); return a; }
        GameObject Child(string name) { var g = new GameObject(name); g.transform.SetParent(root.transform, false); return g; }
        SpatialStage Stage(Vector3 dims, bool flipFront = false, bool flipRight = false, bool flipUp = false)
        {
            var s = Child("Stage").AddComponent<SpatialStage>(); s.dimensionsMeters = dims; s.flipFront = flipFront; s.flipRight = flipRight; s.flipUp = flipUp; return s;
        }
        SpatialPathPlayer Player(SpatialPathAsset asset, SpatialStage stage) { var p = Child("Mover").AddComponent<SpatialPathPlayer>(); p.asset = asset; p.stage = stage; return p; }
        static void Near(Vector3 expected, Vector3 actual) => Assert.That((expected - actual).magnitude, Is.LessThan(1e-4f), "expected " + expected + " got " + actual);

        [Test] public void EvaluateAppliesStageDimensionsFlipsRotationAndScale()
        {
            var stage = Stage(new Vector3(20, 15, 8), flipRight: true);
            stage.transform.SetPositionAndRotation(new Vector3(10, 0, 5), Quaternion.Euler(0, 90, 0)); stage.transform.localScale = Vector3.one * 2;
            var player = Player(Asset(Line), stage);
            player.Evaluate(1); // canonical (.5,.5,.5) -> local (x=-3.75 right flipped, y=2, z=5) -> scale 2 -> (-7.5,4,10) -> yaw 90: (10,4,7.5) -> + (10,0,5)
            Near(new Vector3(20, 4, 12.5f), player.transform.position);
            Assert.AreEqual("", player.LastError);
            Assert.AreEqual(1.0, player.Gain.Value, 1e-9);
            Assert.IsFalse(player.Spread.HasValue);
        }

        [Test] public void EvaluateDoesNotTouchRotationScaleTimeOrPlayState()
        {
            var player = Player(Asset(Line), Stage(new Vector3(20, 15, 8)));
            player.transform.rotation = Quaternion.Euler(10, 20, 30); player.transform.localScale = new Vector3(2, 3, 4);
            player.Evaluate(2);
            Near(new Vector3(7.5f, 4, 10), player.transform.position);
            Assert.AreEqual(Quaternion.Euler(10, 20, 30).eulerAngles.y, player.transform.rotation.eulerAngles.y, 1e-3f);
            Assert.AreEqual(new Vector3(2, 3, 4), player.transform.localScale);
            Assert.AreEqual(0.0, player.Time); Assert.IsFalse(player.IsPlaying);
        }

        [Test] public void InvalidAssetMovesNothingSetsLastErrorAndWarnsOnce()
        {
            var player = Player(Asset(Header + "\"tracks\":[]}"), Stage(Vector3.one * 2));
            player.transform.position = new Vector3(1, 2, 3);
            int warnings = 0; Application.LogCallback hook = (m, s, t) => { if (t == LogType.Warning && m.Contains("Mover")) ++warnings; };
            Application.logMessageReceived += hook;
            try { for (int i = 0; i < 3; ++i) player.Evaluate(1); } finally { Application.logMessageReceived -= hook; }
            Assert.AreEqual(new Vector3(1, 2, 3), player.transform.position);
            StringAssert.Contains("invalid", player.LastError); StringAssert.Contains("no_tracks", player.LastError);
            Assert.AreEqual(1, warnings);
            Assert.IsFalse(player.asset.IsValid); Assert.AreEqual("no_tracks", player.asset.Problems[0].Code);
        }

        [Test] public void MissingAssetStageOrTrackMovesNothing()
        {
            var start = new Vector3(4, 5, 6);
            var noAsset = Player(null, Stage(Vector3.one * 2)); noAsset.transform.position = start; noAsset.Evaluate(1);
            Assert.AreEqual(start, noAsset.transform.position); Assert.IsNotEmpty(noAsset.LastError);
            var noTrack = Player(Asset(Line), noAsset.stage); noTrack.trackId = "missing"; noTrack.transform.position = start; noTrack.Evaluate(1);
            Assert.AreEqual(start, noTrack.transform.position); StringAssert.Contains("missing", noTrack.LastError);
            foreach (var st in root.GetComponentsInChildren<SpatialStage>()) Object.DestroyImmediate(st.gameObject);
            var lone = Child("Lone"); lone.transform.position = start;
            var p = lone.AddComponent<SpatialPathPlayer>(); p.asset = Asset(Line);
            p.Evaluate(1); Assert.AreEqual(start, lone.transform.position); StringAssert.Contains("SpatialStage", p.LastError);
        }

        [Test] public void PlayOnEnableDefaultsOffAndNothingMovesAutomatically()
        {
            var player = Player(Asset(Line), Stage(new Vector3(20, 15, 8)));
            Assert.IsFalse(player.playOnEnable); Assert.AreEqual(1f, player.playbackSpeed); Assert.IsFalse(player.unscaledTime);
            Assert.AreEqual(Vector3.zero, player.transform.position);
            Assert.IsFalse(player.IsPlaying); Assert.AreEqual(0.0, player.Time);
        }

        [Test] public void SeekPauseStopAndPlaySemantics()
        {
            var player = Player(Asset(Line), Stage(new Vector3(20, 15, 8)));
            player.Seek(1); Assert.AreEqual(1.0, player.Time); Near(new Vector3(3.75f, 2, 5), player.transform.position);
            player.Play(); Assert.IsTrue(player.IsPlaying); Assert.AreEqual(1.0, player.Time);
            player.Pause(); Assert.IsFalse(player.IsPlaying); Assert.AreEqual(1.0, player.Time); Near(new Vector3(3.75f, 2, 5), player.transform.position);
            player.Evaluate(2); Assert.AreEqual(1.0, player.Time); Near(new Vector3(7.5f, 4, 10), player.transform.position);
            player.Play(); player.Stop(); Assert.IsFalse(player.IsPlaying); Assert.AreEqual(0.0, player.Time); Near(Vector3.zero, player.transform.position);
            player.Seek(-5); Assert.AreEqual(0.0, player.Time);
            player.Seek(double.NaN); Assert.AreEqual(0.0, player.Time);
        }

        [Test] public void TrackSelectionAndDuration()
        {
            var asset = Asset(Line); var stage = Stage(new Vector3(20, 15, 8));
            var player = Player(asset, stage);
            CollectionAssert.AreEqual(new[] { "a", "b" }, asset.TrackIds);
            player.Evaluate(1); Near(new Vector3(3.75f, 2, 5), player.transform.position); Assert.AreEqual(2.0, player.Duration);   // empty = first track
            player.trackId = "b"; player.Evaluate(.5f); Near(new Vector3(3.75f, 0, 0), player.transform.position); Assert.AreEqual(1.0, player.Duration);
            Assert.IsFalse(player.Gain.HasValue);
        }

        [Test] public void StageIsAutoFoundInSameScene()
        {
            var stage = Stage(new Vector3(20, 15, 8));
            var player = Player(Asset(Line), null);
            Assert.IsNull(player.stage);
            player.Evaluate(2);
            Assert.AreSame(stage, player.stage);
            Near(new Vector3(7.5f, 4, 10), player.transform.position);
        }

        [Test] public void SpatialObjectBoundTransformIsMoved()
        {
            var stage = Stage(new Vector3(20, 15, 8));
            var bound = Child("Bound").transform;
            var go = Child("Obj"); var so = go.AddComponent<SpatialObject>(); so.boundTransform = bound;
            var player = go.AddComponent<SpatialPathPlayer>(); player.asset = Asset(Line); player.stage = stage;
            player.Evaluate(2);
            Near(new Vector3(7.5f, 4, 10), bound.position); Near(Vector3.zero, go.transform.position);
            var explicitTarget = Child("Explicit").transform; player.target = explicitTarget; player.Evaluate(1);
            Near(new Vector3(3.75f, 2, 5), explicitTarget.position);
        }

        [Test] public void AssetCacheInvalidatesWhenJsonChanges()
        {
            var asset = Asset("not json");
            Assert.IsFalse(asset.IsValid); Assert.AreEqual("bad_json", asset.Problems[0].Code);
            asset.SetJson(Line); Assert.IsTrue(asset.IsValid); Assert.AreEqual(2, asset.TrackIds.Length);
            Assert.AreEqual(1.0, asset.Sample("a", 1).Gain.Value, 1e-9);
            asset.SetJson(Header + "\"tracks\":[]}"); Assert.IsFalse(asset.IsValid);
            Assert.Throws<System.InvalidOperationException>(() => asset.Sample("a", 0));
        }

        [Test] public void ImporterValidatesBytesWithoutCreatingAssets()
        {
            Assert.IsNotNull(SpatialPathImporter.ValidateBytes(Encoding.UTF8.GetBytes(Line), out var error)); Assert.IsNull(error);
            Assert.IsNull(SpatialPathImporter.ValidateBytes(Encoding.UTF8.GetBytes(Header + "\"tracks\":[]}"), out error)); StringAssert.Contains("no_tracks", error);
            Assert.IsNull(SpatialPathImporter.ValidateBytes(new byte[] { 0xff }, out error)); Assert.IsNotEmpty(error);
        }
    }
}
