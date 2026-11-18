// SILENT: no PCM/source assigned, so opt-in playback can only fail before device start. MIT.
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace NPLAudio.SpatialFabric.Tests
{
    public sealed class PlaybackStartupTests
    {
        [UnityTest] public IEnumerator StartupIsOptInOnceAndDisabledCuesWaitForEnable()
        {
            SceneInventory.RegisterMainThread();
            var idle=new GameObject("Default idle cue");var idleCue=idle.AddComponent<PlaybackCue>();
            var opted=new GameObject("Opted in silent cue");opted.SetActive(false);var cue=opted.AddComponent<PlaybackCue>();cue.playOnStart=true;
            try
            {
                yield return null;
                Assert.That(idleCue.playOnStart,Is.False);Assert.That(idleCue.LastError,Is.Empty);Assert.That(cue.LastError,Is.Empty);
                opted.SetActive(true);yield return null;
                Assert.That(cue.LastError,Does.Contain("No prepared PCM"));Assert.That(cue.IsPrepared,Is.False);Assert.That(cue.IsPlaying,Is.False);
                var request=typeof(PlaybackCue).GetProperty("AudioRequestVersion",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                opted.SetActive(false);opted.SetActive(true);uint afterEnable=(uint)request.GetValue(cue);
                yield return null;Assert.That((uint)request.GetValue(cue),Is.EqualTo(afterEnable),"Start must not be repeated on re-enable");
                Assert.That(idleCue.IsPrepared,Is.False);Assert.That(idleCue.LastError,Is.Empty);
            }
            finally {Object.Destroy(idle);Object.Destroy(opted);}
        }
    }
}
