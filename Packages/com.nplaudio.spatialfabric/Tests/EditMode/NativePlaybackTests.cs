// SILENT integration tests: offline PCM and intentionally invalid device only. MIT.
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using NPLAudio.SpatialFabric.Audio;
namespace NPLAudio.SpatialFabric.Tests
{
    public sealed class NativePlaybackTests
    {
        Scene scene;
        GameObject root;
        PreparedAudioAsset asset;
        [SetUp] public void Setup()
        {
            SceneInventory.RegisterMainThread();NativeAudio.Probe();
            if(Application.platform!=RuntimePlatform.WindowsEditor || !NativeAudio.IsAvailable)Assert.Ignore("Build/install Windows native plugin to run silent native integration tests");
            scene=EditorSceneManager.NewPreviewScene();root=new GameObject("Native audio test");SceneManager.MoveGameObjectToScene(root,scene);
            asset=ScriptableObject.CreateInstance<PreparedAudioAsset>();asset.SetPcm(new PcmData(new[]{.25f,-.5f,.75f,-1f},2,48000),"test PCM");
        }
        [TearDown] public void Cleanup()
        {
            if(root!=null)Object.DestroyImmediate(root);if(asset!=null)Object.DestroyImmediate(asset);
            if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);
        }
        [Test] public void ExplicitDeviceRefreshParsesNativeUtf8InventoryWithoutPlayback()
        {
            var devices=NativeAudio.RefreshDevices();Assert.That(NativeAudio.DeviceEnumerationError,Is.Empty);
            foreach(var device in devices) {Assert.That(device.id,Is.Not.Empty);Assert.That(device.name,Is.Not.Empty);}
        }
        [Test] public void NativeOfflineRenderPreservesSourceChannelsAndSelectedOutput()
        {
            using(var buffer=NativeAudio.Prepare(asset.Snapshot()))
            {
                double position=0;var samples=NativeAudio.RenderOffline(buffer,3,4,48000,0,false,ref position,out bool ended);
                Assert.That(ended,Is.True);Assert.That(samples,Is.EqualTo(new[]{.25f,-.5f,0,0,.75f,-1,0,0,0,0,0,0}));
                position=0;samples=NativeAudio.RenderOffline(buffer,2,4,48000,3,false,ref position,out ended);
                Assert.That(samples,Is.EqualTo(new[]{0f,0,.25f,0,0,0,.75f,0}));
            }
        }
        [Test] public void CueReadinessInvalidatesWhenPreparedSourceRevisionChanges()
        {
            var cue=root.AddComponent<PlaybackCue>();cue.preparedAudio=asset;Assert.That(cue.Prepare(),Is.True);Assert.That(cue.IsPrepared,Is.True);
            asset.SetPcm(new PcmData(new[]{.75f},1,48000),"new revision");Assert.That(cue.IsPrepared,Is.False);Assert.That(cue.IsPlaying,Is.False);cue.ReleaseAudio();
        }
        [Test] public void NativePreparedCopySurvivesManagedAssetChanges()
        {
            using(var buffer=NativeAudio.Prepare(asset.Snapshot()))
            {
                asset.SetPcm(new PcmData(new[]{1f},1,48000),"replacement");double position=0;
                Assert.That(NativeAudio.RenderOffline(buffer,1,2,48000,0,false,ref position,out _),Is.EqualTo(new[]{.25f,-.5f}));
            }
        }
        [Test] public void DiscoveryNeverPreparesAssetOrEnumeratesDevices()
        {
            var cue=root.AddComponent<PlaybackCue>();cue.preparedAudio=asset;var manager=root.AddComponent<SpatialFabricManager>();
            string before=EditorJsonUtility.ToJson(cue);var devices=NativeAudio.CachedDevices;
            var page=JsonUtility.FromJson<ScenePage>(SceneInventory.GetJson(manager));
            Assert.That(cue.IsPrepared,Is.False);Assert.That(cue.IsPlaying,Is.False);Assert.That(NativeAudio.CachedDevices,Is.SameAs(devices));
            Assert.That(EditorJsonUtility.ToJson(cue),Is.EqualTo(before));Assert.That(page.objects.Length,Is.EqualTo(2));
        }
        [UnityTest] public IEnumerator MissingDeviceFailsWithoutPlayingOrAutoReplay()
        {
            var cue=root.AddComponent<PlaybackCue>();cue.preparedAudio=asset;cue.configuredDeviceId="spatialfabric-intentionally-invalid-device-no-fallback";cue.autoReplay=true;
            int starts=0;System.Action<PlaybackCue,bool> observer=(c,playing)=>{if(c==cue && playing)++starts;};PlaybackEvents.StateChanged+=observer;
            try
            {
                Assert.That(cue.TryPlay(),Is.True);float deadline=Time.realtimeSinceStartup+5;
                while(cue.WorkerState==AudioWorkerState.Starting && Time.realtimeSinceStartup<deadline) {cue.Tick(Time.realtimeSinceStartupAsDouble);yield return null;}
                Assert.That(cue.WorkerState,Is.EqualTo(AudioWorkerState.Failed),cue.LastError);Assert.That(starts,Is.Zero);
                Assert.That(cue.IsPlaying,Is.False);Assert.That(cue.ReplayPending,Is.False);Assert.That(cue.LastError,Does.Contain("no default fallback"));
                cue.Stop();Assert.That(cue.ReplayPending,Is.False);cue.ReleaseAudio();Assert.That(cue.IsPrepared,Is.False);
            }
            finally {PlaybackEvents.StateChanged-=observer;}
        }
    }
}
