// SILENT live-input tests: inventory and intentionally invalid devices only. No microphone is ever opened. MIT.
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using NPLAudio.SpatialFabric.Audio;
using NPLAudio.SpatialFabric.Osc;
namespace NPLAudio.SpatialFabric.Tests
{
    public sealed class LiveInputTests
    {
        Scene scene;
        GameObject root;
        [SetUp] public void Setup()
        {
            SceneInventory.RegisterMainThread();NativeLive.Probe();
            if(Application.platform!=RuntimePlatform.WindowsEditor || !NativeLive.IsAvailable)Assert.Ignore("Build/install the Windows native plugin (with live input) to run silent live-input tests");
            scene=EditorSceneManager.NewPreviewScene();root=new GameObject("Live input test");SceneManager.MoveGameObjectToScene(root,scene);
        }
        [TearDown] public void Cleanup()
        {
            if(root!=null)Object.DestroyImmediate(root);
            if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);
        }
        [Test] public void ExplicitInputRefreshParsesInventoryWithoutOpeningAnyStream()
        {
            var inputs=NativeLive.RefreshInputs();Assert.That(NativeLive.InputEnumerationError,Is.Empty);
            foreach(var device in inputs) {Assert.That(device.id,Is.Not.Empty);Assert.That(device.name,Is.Not.Empty);Assert.That(device.channels,Is.GreaterThan(0));}
        }
        [UnityTest] public IEnumerator MissingInputDeviceFailsWithoutFalseLiveOrState()
        {
            var cue=root.AddComponent<LiveInputCue>();cue.inputDeviceId="spatialfabric-intentionally-invalid-input-no-fallback";cue.outputDeviceId="spatialfabric-intentionally-invalid-output-no-fallback";
            int lives=0;System.Action<LiveInputCue,bool> observer=(c,live)=>{if(c==cue && live)++lives;};LiveInputEvents.StateChanged+=observer;
            try
            {
                Assert.That(cue.IsLive,Is.False);Assert.That(cue.TryStart(),Is.True); // a request, not an acknowledgement
                float deadline=Time.realtimeSinceStartup+5;
                while(cue.WorkerState==AudioWorkerState.Starting && Time.realtimeSinceStartup<deadline) {cue.Tick(Time.realtimeSinceStartupAsDouble);yield return null;}
                Assert.That(cue.WorkerState,Is.EqualTo(AudioWorkerState.Failed),cue.LastError);Assert.That(lives,Is.Zero);Assert.That(cue.IsLive,Is.False);
                Assert.That(cue.LastError,Does.Contain("no default fallback"));cue.Stop();Assert.That(cue.IsLive,Is.False);cue.ReleaseAudio();
            }
            finally {LiveInputEvents.StateChanged-=observer;}
        }
        [Test] public void GainIsValidatedAndOscGainIsBounded()
        {
            var cue=root.AddComponent<LiveInputCue>();cue.gain=1;
            Assert.That(cue.SetGain(5),Is.False);Assert.That(cue.SetGain(float.NaN),Is.False);Assert.That(cue.SetGain(-.1f),Is.False);Assert.That(cue.gain,Is.EqualTo(1));
            Assert.That(cue.SetGain(.5f),Is.True);Assert.That(cue.gain,Is.EqualTo(.5f));
            Assert.That(PlaybackControl.Gain(new OscMessage("/x/gain",OscArg.Float(.25f)),out float g) && g==.25f,Is.True);
            Assert.That(PlaybackControl.Gain(new OscMessage("/x/gain",OscArg.Int(2)),out g) && g==2,Is.True);
            Assert.That(PlaybackControl.Gain(new OscMessage("/x/gain",OscArg.Float(4.1f)),out _),Is.False);
            Assert.That(PlaybackControl.Gain(new OscMessage("/x/gain",OscArg.Int(-1)),out _),Is.False);
            Assert.That(PlaybackControl.Gain(new OscMessage("/x/gain"),out _),Is.False);
        }
        [Test] public void DiscoveryReportsLiveInputWithoutOpeningAnythingOrMutating()
        {
            var cue=root.AddComponent<LiveInputCue>();cue.oscBase="/test/live";cue.inputChannel=2;var manager=root.AddComponent<SpatialFabricManager>();
            string before=EditorJsonUtility.ToJson(cue);var inputs=NativeLive.CachedInputs;
            var page=JsonUtility.FromJson<ScenePage>(SceneInventory.GetJson(manager));
            var row=System.Array.Find(page.objects,o=>o.kind=="live_input");Assert.That(row,Is.Not.Null);
            Assert.That(row.controls.gain,Is.EqualTo("/test/live/gain"));Assert.That(row.input_channel,Is.EqualTo(2));Assert.That(row.playing,Is.False);
            Assert.That(cue.IsLive,Is.False);Assert.That(NativeLive.CachedInputs,Is.SameAs(inputs));Assert.That(EditorJsonUtility.ToJson(cue),Is.EqualTo(before));
        }
    }
}
