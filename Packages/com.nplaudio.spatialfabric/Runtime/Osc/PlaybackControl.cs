// Exact AudioLoom wire trigger rules: float precedence, play >= .5, loop > .5. MIT.
namespace NPLAudio.SpatialFabric.Osc
{
    public static class PlaybackControl
    {
        public static bool Play(OscMessage message)
        {
            if(message.Arguments.Length==0)return true;
            foreach(var arg in message.Arguments)if(arg.Tag=='f')return (float)arg.Value>=.5f;
            foreach(var arg in message.Arguments)if(arg.Tag=='i')return (int)arg.Value>=1;
            return false;
        }
        /// <summary>Live-input gain: first float anywhere, else first int; must be finite 0..4. Anything else is ignored.</summary>
        public static bool Gain(OscMessage message,out float gain)
        {
            gain=0;
            foreach(var arg in message.Arguments)if(arg.Tag=='f'){gain=(float)arg.Value;return !float.IsNaN(gain) && !float.IsInfinity(gain) && gain>=0 && gain<=4;}
            foreach(var arg in message.Arguments)if(arg.Tag=='i'){gain=(int)arg.Value;return gain>=0 && gain<=4;}
            return false;
        }
        public static bool Loop(OscMessage message)
        {
            foreach(var arg in message.Arguments)if(arg.Tag=='f')return (float)arg.Value>.5f;
            foreach(var arg in message.Arguments)if(arg.Tag=='i')return (int)arg.Value>=1;
            return false;
        }
    }
}
