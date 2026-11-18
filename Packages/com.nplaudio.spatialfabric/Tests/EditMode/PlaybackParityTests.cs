// SILENT: no PCM is prepared; tests never open physical playback or capture. MIT.
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using NPLAudio.SpatialFabric.Audio;
namespace NPLAudio.SpatialFabric.Tests
{
    public sealed class PlaybackParityTests
    {
        Scene scene;
        GameObject root;
        PlaybackCue cue;
        [SetUp] public void Setup()
        {
            SceneInventory.RegisterMainThread();scene=EditorSceneManager.NewPreviewScene();
            root=new GameObject("Parity");SceneManager.MoveGameObjectToScene(root,scene);cue=root.AddComponent<PlaybackCue>();
        }
        [TearDown] public void Cleanup(){UnityEngine.Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
        [Test] public void BlankAddressesAreUniqueAndExplicitLegacyBasesSurviveSerialization()
        {
            var child=new GameObject("Parity");child.transform.SetParent(root.transform);var other=child.AddComponent<PlaybackCue>();
            Assert.That(cue.oscBase,Is.Empty);Assert.That(cue.ValidBase,Is.True);Assert.That(other.EffectiveOscBase,Is.Not.EqualTo(cue.EffectiveOscBase));
            string derived=cue.EffectiveOscBase;Assert.That(cue.EffectiveOscBase,Is.EqualTo(derived));
            // Unity 6000.6 Editor JSON wraps MonoBehaviour fields; a flat partial
            // object is silently ignored. Round-trip the actual serialized shape.
            cue.oscBase="/unity/playback/cue";
            string serialized=EditorJsonUtility.ToJson(cue);
            cue.oscBase="";
            Assert.That(cue.EffectiveOscBase,Is.EqualTo(derived));
            EditorJsonUtility.FromJsonOverwrite(serialized,cue);
            Assert.That(cue.EffectiveOscBase,Is.EqualTo("/unity/playback/cue"));
            Assert.That(cue.oscBase,Is.EqualTo("/unity/playback/cue"));
        }
        [Test] public void ManagerReportsPlaybackLiveAndDisabledDuplicatesWithoutChangingOverrides()
        {
            var child=new GameObject("Duplicate");child.transform.SetParent(root.transform);var other=child.AddComponent<PlaybackCue>();
            var manager=root.AddComponent<SpatialFabricManager>();var live=root.AddComponent<LiveInputCue>();
            cue.oscBase=other.oscBase=live.oscBase="/shared";other.enabled=false;
            var issues=manager.PlaybackAddressDiagnostics();Assert.That(issues.Count,Is.EqualTo(1));Assert.That(issues[0],Does.Contain("3 cues"));
            other.oscBase="";live.oscBase="/live";Assert.That(manager.PlaybackAddressDiagnostics(),Is.Empty);
            Assert.That(cue.oscBase,Is.EqualTo("/shared"));Assert.That(manager.IsListening,Is.False);
        }
        [Test] public void OptInNeverStartsInEditorAndIdleRoutingDoesNotAutoplay()
        {
            Assert.That(cue.playOnStart,Is.False);cue.playOnStart=true;
            typeof(PlaybackCue).GetMethod("Start",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(cue,null);
            Assert.That(cue.LastError,Is.Empty);Assert.That(cue.IsPrepared,Is.False);
            cue.configuredDeviceId="intentionally-missing";cue.outputChannel=4;cue.exclusiveRequested=true;cue.bufferMsRequested=10;
            cue.Tick(10);Assert.That(cue.WorkerState,Is.EqualTo(AudioWorkerState.Stopped));Assert.That(cue.LastError,Is.Empty);
            Assert.That(cue.IsPrepared,Is.False);Assert.That(cue.ReplayPending,Is.False);
        }
        [Test] public void RandomDelayUsesOneDeterministicDrawAndStopCancelsReplay()
        {
            cue.randomReplayDelay=true;cue.replayDelayMin=2;cue.replayDelayMax=5;cue.autoReplay=true;
            var observation=(PlaybackObservation)typeof(PlaybackCue).GetField("observation",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cue);
            var select=(Func<double>)Delegate.CreateDelegate(typeof(Func<double>),cue,"SelectReplayDelay");
            uint expected=1;double delay=PlaybackPolicy.ReplayDelay(true,0,2,5,ref expected);
            observation.Observe(AudioWorkerState.Stopped,AudioEndReason.NaturalEnd,1,1,true,true,false,0,10,select);
            observation.Observe(AudioWorkerState.Stopped,AudioEndReason.NaturalEnd,1,1,true,true,false,0,11,select);
            Assert.That(observation.TakeReplay(10+delay-.000001),Is.False);Assert.That(observation.TakeReplay(10+delay),Is.True);
            Assert.That((uint)typeof(PlaybackCue).GetField("replayRandomState",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cue),Is.EqualTo(expected));
            observation.Observe(AudioWorkerState.Stopped,AudioEndReason.NaturalEnd,2,2,true,true,false,0,20,select);
            cue.Stop();Assert.That(cue.ReplayPending,Is.False);Assert.That(cue.IsPrepared,Is.False);
        }
        [Test] public void SourceReplacementIsDetectedWithoutPreparingOrStartingAudio()
        {
            var property=typeof(PlaybackCue).GetProperty("SourceChanged",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.That(property.GetValue(cue),Is.False);
            var asset=ScriptableObject.CreateInstance<PreparedAudioAsset>();
            try
            {
                cue.preparedAudio=asset;
                Assert.That(property.GetValue(cue),Is.True);
                cue.Tick(0); // Idle source assignment never starts or prepares audio.
                Assert.That(cue.IsPrepared,Is.False);Assert.That(cue.IsPlaying,Is.False);
                typeof(PlaybackCue).GetField("cachedAsset",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(cue,asset);
                typeof(PlaybackCue).GetField("cachedRevision",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(cue,asset.Revision);
                Assert.That(property.GetValue(cue),Is.False);
                cue.preparedAudio=null;
                Assert.That(property.GetValue(cue),Is.True);
            }
            finally {UnityEngine.Object.DestroyImmediate(asset);}
        }
        [Test] public void RoutePolicyRestartsOnlyActiveRequests()
        {
            foreach(AudioWorkerState state in Enum.GetValues(typeof(AudioWorkerState)))
            {
                Assert.That(PlaybackPolicy.RestartRoute(false,state),Is.False);
                Assert.That(PlaybackPolicy.RestartRoute(true,state),Is.EqualTo(state==AudioWorkerState.Starting || state==AudioWorkerState.Playing || state==AudioWorkerState.Draining));
            }
        }
    }
}
