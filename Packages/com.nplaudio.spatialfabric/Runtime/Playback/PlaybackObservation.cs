// Pure observed-state/replay policy. Synthetic traces in tests do not open/acknowledge hardware. MIT.
using System;
namespace NPLAudio.SpatialFabric.Audio
{
    public enum AudioWorkerState { Stopped=0, Starting=1, Playing=2, Draining=3, Failed=4 }
    public enum AudioEndReason { None=0, NaturalEnd=1, Cancelled=2, DeviceError=3 }
    public sealed class PlaybackObservation
    {
        ulong starts,ends;
        double replayAt;
        public bool IsPlaying { get; private set; }
        public bool ReplayPending { get; private set; }
        public event Action<bool> StateChanged;
        public void CancelReplay() {ReplayPending=false;}
        public void Reset() {starts=ends=0;ReplayPending=false;IsPlaying=false;}
        public void ApplyReplayPolicy(bool autoReplay,bool loop) {if(!autoReplay || loop)CancelReplay();}
        public bool TakeReplay(double now) {if(!ReplayPending || now<replayAt)return false;ReplayPending=false;return true;}
        public void Observe(AudioWorkerState state,AudioEndReason reason,ulong started,ulong ended,bool allowReplay,bool autoReplay,bool loop,double delay,double now)
        {
            if(started!=starts) {starts=started;IsPlaying=true;StateChanged?.Invoke(true);}
            if(ended!=ends)
            {
                ends=ended;IsPlaying=false;StateChanged?.Invoke(false);
                if(allowReplay && autoReplay && !loop && reason==AudioEndReason.NaturalEnd)
                {
                    if(double.IsNaN(delay) || double.IsInfinity(delay))delay=0;
                    replayAt=now+Math.Max(0,delay);ReplayPending=true;
                }
            }
            if(state==AudioWorkerState.Failed || state==AudioWorkerState.Stopped)IsPlaying=false;
            ApplyReplayPolicy(autoReplay,loop);
        }
    }
}
