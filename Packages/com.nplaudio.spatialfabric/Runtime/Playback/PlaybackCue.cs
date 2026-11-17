// Direct-output cue. Never routes PCM through Unity AudioSource/mixer. MIT.
using System;
using UnityEngine;
using NPLAudio.SpatialFabric.Audio;
using NPLAudio.SpatialFabric.Osc;
namespace NPLAudio.SpatialFabric
{
    public static class PlaybackEvents
    {
        public static event Action<PlaybackCue,bool> StateChanged;
        internal static void Emit(PlaybackCue cue,bool playing) {if(StateChanged==null)return;foreach(Action<PlaybackCue,bool> observer in StateChanged.GetInvocationList())try{observer(cue,playing);}catch(Exception e){Debug.LogException(e);} }
    }
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class PlaybackCue : SpatialIdentity
    {
        [Tooltip("Preferred: immutable PCM asset imported explicitly from WAV. Discovery never prepares it.")]
        public PreparedAudioAsset preparedAudio;
        [Tooltip("Optional Unity clip source; requires Decompress On Load and already-loaded samples. No AudioSource is created.")]
        public AudioClip clip;
        public string oscBase = "/unity/playback/cue";
        public string configuredDeviceId = "";
        [Min(0)] public int outputChannel;
        public bool exclusiveRequested;
        [Range(0,2000)] public int bufferMsRequested;
        public bool loop, autoReplay;
        [Min(0)] public float replayDelaySeconds = 1;
        public string LastError { get; private set; } = "";
        public bool BackendAvailable => NativeAudio.IsAvailable;
        public bool IsPrepared => buffer != null && !buffer.IsClosed && !buffer.IsInvalid && (preparedAudio!=null || clip!=null) && cachedAsset==preparedAudio && cachedClip==clip && (preparedAudio==null || cachedRevision==preparedAudio.Revision);
        public bool IsPlaying => observation.IsPlaying;
        public bool ReplayPending => observation.ReplayPending;
        public AudioWorkerState WorkerState { get; private set; }
        public AudioEndReason EndReason { get; private set; }
        public int ActualOutputChannels { get; private set; }
        public int ActualOutputRate { get; private set; }
        public int ActualBufferFrames { get; private set; }
        public bool ExclusiveActive { get; private set; }
        public bool ValidBase => OscCodec.ValidAddress(oscBase);
        // Changes whenever a local or OSC action cancels/replaces a start request.
        internal uint AudioRequestVersion { get; private set; }
        NativeAudio.Buffer buffer;
        NativeAudio.Player player;
        PreparedAudioAsset cachedAsset;
        AudioClip cachedClip;
        string cachedRevision;
        readonly PlaybackObservation observation = new PlaybackObservation();
        double nextPoll;
        void OnEnable() {observation.StateChanged += OnObservedState;}
        void OnObservedState(bool playing) {PlaybackEvents.Emit(this,playing);}
        static double Clock => Time.realtimeSinceStartupAsDouble;
        public bool Prepare()
        {
            if (!SceneInventory.IsMainThread) {LastError="PCM preparation requires the Unity main thread";return false;}
            Stop(); ReleaseBuffer();
            try
            {
                if(!isActiveAndEnabled)throw new InvalidOperationException("Cue is disabled");
                PcmData pcm;
                if(preparedAudio!=null) pcm=preparedAudio.Snapshot();
                else if(clip!=null)
                {
                    if(clip.loadType!=AudioClipLoadType.DecompressOnLoad || clip.loadState!=AudioDataLoadState.Loaded) throw new InvalidOperationException("Clip must be Decompress On Load with samples already loaded; import a Prepared PCM WAV asset instead");
                    long count=(long)clip.samples*clip.channels;
                    if(count<1 || count>WavDecoder.MaxSamples)throw new InvalidOperationException("Clip exceeds PCM sample bounds");
                    var samples=new float[(int)count];if(!clip.GetData(samples,0))throw new InvalidOperationException("Clip PCM read failed");
                    pcm=new PcmData(samples,clip.channels,clip.frequency);
                }
                else throw new InvalidOperationException("No prepared PCM asset or loaded clip assigned; no audio was started");
                buffer=NativeAudio.Prepare(pcm);cachedAsset=preparedAudio;cachedClip=clip;cachedRevision=preparedAudio!=null?preparedAudio.Revision:"";LastError="";return true;
            }
            catch(Exception e) when(e is ArgumentException || e is InvalidOperationException || e is OverflowException || e is OutOfMemoryException) {LastError=e.Message;ReleaseBuffer();return false;}
        }
        public bool TryPlay()
        {
            if(!SceneInventory.IsMainThread) {LastError="Playback requires the Unity main thread";return false;}
            Stop();
            if(!isActiveAndEnabled) {LastError="Cue is disabled";return false;}
            bool stale=!IsPrepared || cachedAsset!=preparedAudio || cachedClip!=clip || preparedAudio!=null && cachedRevision!=preparedAudio.Revision;
            if(stale && !Prepare())return false;
            try
            {
                if(player==null || player.IsClosed) {player=NativeAudio.CreatePlayer();observation.Reset();}
                if(!player.Start(buffer,configuredDeviceId,outputChannel,exclusiveRequested,bufferMsRequested,loop)) {Observe(Clock,false);return false;}
                WorkerState=AudioWorkerState.Starting;EndReason=AudioEndReason.None;LastError="";nextPoll=0;return true; // Request accepted, not Playing acknowledgement.
            }
            catch(Exception e) when(e is ArgumentException || e is InvalidOperationException) {LastError=e.Message;return false;}
        }
        public void Stop()
        {
            unchecked {++AudioRequestVersion;}
            observation.CancelReplay();
            if(player!=null && !player.IsClosed) {player.Stop();Observe(Clock,false);}
        }
        public void SetLoop(bool value) {loop=value;if(value)observation.CancelReplay();if(player!=null && !player.IsClosed)player.SetLoop(value);}
        public void Tick(double now)
        {
            if(!isActiveAndEnabled)return;
            if(player!=null && !player.IsClosed && now>=nextPoll) {nextPoll=now+1.0/60;player.SetLoop(loop);Observe(now,true);}
            observation.ApplyReplayPolicy(autoReplay,loop);
            if(observation.TakeReplay(now))TryPlay();
        }
        void Observe(double now,bool allowReplay)
        {
            AudioStatus s=player.Poll();WorkerState=s.State;EndReason=s.Reason;ActualOutputChannels=s.OutputChannels;ActualOutputRate=s.OutputRate;ActualBufferFrames=s.BufferFrames;ExclusiveActive=s.ExclusiveActive;
            if(!string.IsNullOrEmpty(s.Error))LastError=s.Error;
            // Sequence counters preserve real starts/ends even when a short clip completes between editor ticks.
            observation.Observe(s.State,s.Reason,s.StartedSequence,s.EndedSequence,allowReplay,autoReplay,loop,replayDelaySeconds,now);
        }
        void ReleaseBuffer() {buffer?.Dispose();buffer=null;cachedAsset=null;cachedClip=null;cachedRevision=null;}
        public void ReleaseAudio()
        {
            Stop();player?.Dispose();player=null;ReleaseBuffer();observation.Reset();
        }
        void Update() {if(Application.isPlaying)Tick(Clock);}
        void OnDisable() {ReleaseAudio();observation.StateChanged -= OnObservedState;}
        void OnDestroy() {ReleaseAudio();}
    }
}
