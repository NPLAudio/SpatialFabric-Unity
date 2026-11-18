// Live input -> direct-output cue. Never uses Unity Microphone/AudioSource/mixer. MIT.
// Starting is always explicit; it may trigger the OS microphone permission prompt.
using System;
using UnityEngine;
using NPLAudio.SpatialFabric.Audio;
using NPLAudio.SpatialFabric.Osc;
namespace NPLAudio.SpatialFabric
{
    public static class LiveInputEvents
    {
        public static event Action<LiveInputCue,bool> StateChanged;
        internal static void Emit(LiveInputCue cue,bool live) {if(StateChanged==null)return;foreach(Action<LiveInputCue,bool> observer in StateChanged.GetInvocationList())try{observer(cue,live);}catch(Exception error){Debug.LogException(error,cue);}}
    }
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class LiveInputCue : SpatialIdentity
    {
        public string oscBase = "/unity/live/input";
        [Tooltip("Exact capture endpoint ID. Empty selects the system default input. Configured IDs never fall back.")]
        public string inputDeviceId = "";
        [Tooltip("0 = all input channels; 1..N = only that input channel.")]
        [Min(0)] public int inputChannel;
        [Tooltip("Exact render endpoint ID. Empty selects the system default output.")]
        public string outputDeviceId = "";
        [Tooltip("0 = corresponding channels; 1..N = the selected input to exactly one physical output.")]
        [Min(0)] public int outputChannel;
        public bool exclusiveOutputRequested;
        [Range(0,2000)] public int bufferMsRequested;
        [Range(0,4)] public float gain = 1;
        public string LastError { get; private set; } = "";
        public bool BackendAvailable => NativeLive.IsAvailable;
        public bool IsLive => observation.IsPlaying;
        public AudioWorkerState WorkerState { get; private set; }
        public AudioEndReason EndReason { get; private set; }
        public int ActualInputChannels { get; private set; }
        public int ActualInputRate { get; private set; }
        public int ActualOutputChannels { get; private set; }
        public int ActualOutputRate { get; private set; }
        public float InputPeak { get; private set; }
        public ulong Underruns { get; private set; }
        public ulong Overruns { get; private set; }
        public float LatencyMilliseconds { get; private set; }
        public AudioOutputMode OutputMode { get; private set; } = AudioOutputMode.Unavailable;
        public double OutputLatencyMilliseconds { get; private set; } = -1;
        public bool ValidBase => OscCodec.ValidAddress(oscBase);
        internal uint AudioRequestVersion { get; private set; }
        NativeLive.Session session;
        readonly PlaybackObservation observation = new PlaybackObservation();
        double nextPoll;
        void OnEnable() {observation.StateChanged += OnObservedState;}
        void OnObservedState(bool live) {LiveInputEvents.Emit(this,live);}
        static double Clock => Time.realtimeSinceStartupAsDouble;
        /// <summary>Submits a start request. IsLive becomes true only after the native worker really started.</summary>
        public bool TryStart()
        {
            if(!SceneInventory.IsMainThread) {LastError="Live input requires the Unity main thread";return false;}
            Stop();
            if(!isActiveAndEnabled) {LastError="Cue is disabled";return false;}
            try
            {
                if(session==null || session.IsClosed) {session=NativeLive.Create();observation.Reset();}
                if(!session.Start(inputDeviceId,inputChannel,outputDeviceId,outputChannel,exclusiveOutputRequested,bufferMsRequested,gain)) {Observe(Clock);return false;}
                WorkerState=AudioWorkerState.Starting;EndReason=AudioEndReason.None;LastError="";nextPoll=0;return true;
            }
            catch(Exception e) when(e is ArgumentException || e is InvalidOperationException) {LastError=e.Message;return false;}
        }
        public void Stop() {unchecked {++AudioRequestVersion;}if(session!=null && !session.IsClosed) {session.Stop();Observe(Clock);}}
        public bool SetGain(float value)
        {
            if(float.IsNaN(value) || float.IsInfinity(value) || value<0 || value>4) {LastError="Gain must be finite 0..4";return false;}
            gain=value;if(session!=null && !session.IsClosed)session.SetGain(value);return true;
        }
        public void Tick(double now)
        {
            if(!isActiveAndEnabled || session==null || session.IsClosed || now<nextPoll)return;
            nextPoll=now+1.0/30;Observe(now);
        }
        void Observe(double now)
        {
            LiveStatus s=session.Poll();WorkerState=s.State;EndReason=s.Reason;ActualInputChannels=s.InputChannels;ActualInputRate=s.InputRate;ActualOutputChannels=s.OutputChannels;ActualOutputRate=s.OutputRate;
            OutputMode=s.OutputMode;OutputLatencyMilliseconds=s.OutputLatencyMilliseconds;
            InputPeak=Mathf.Max(InputPeak*0.9f,s.InputPeak);Underruns=s.Underruns;Overruns=s.Overruns;LatencyMilliseconds=s.InputRate>0?s.LatencyFrames*1000f/s.InputRate:0;
            if(!string.IsNullOrEmpty(s.Error))LastError=s.Error;
            observation.Observe(s.State,s.Reason,s.StartedSequence,s.EndedSequence,false,false,false,0,now);
        }
        public void ReleaseAudio() {Stop();session?.Dispose();session=null;observation.Reset();InputPeak=0;}
        void Update() {if(Application.isPlaying)Tick(Clock);}
        void OnDisable() {ReleaseAudio();observation.StateChanged -= OnObservedState;}
        void OnDestroy() {ReleaseAudio();}
    }
}
