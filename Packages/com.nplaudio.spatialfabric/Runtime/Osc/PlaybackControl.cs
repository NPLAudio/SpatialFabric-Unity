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
        public static bool Loop(OscMessage message)
        {
            foreach(var arg in message.Arguments)if(arg.Tag=='f')return (float)arg.Value>.5f;
            foreach(var arg in message.Arguments)if(arg.Tag=='i')return (int)arg.Value>=1;
            return false;
        }
    }
}
