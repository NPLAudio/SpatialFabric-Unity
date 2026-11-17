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
        /// <summary>Pure command decision; received play means submit a request, not observed playing.</summary>
        public static string Decide(string command,OscMessage message,bool live)
        {
            switch(command)
            {
                case "ack": case "state": return "ignored";
                case "status": return message.Arguments.Length==0?"received":"rejected";
                case "play": return Play(message)?"received":"ignored";
                case "stop": return "received";
                case "loop": return live?"rejected":message.Arguments.Length==0 || HasNumber(message)?"received":"ignored";
                case "gain": return live && Gain(message,out _)?"received":"rejected";
                default: return "rejected";
            }
        }
        static bool HasNumber(OscMessage message)
        {
            foreach(var arg in message.Arguments)if(arg.Tag=='f' || arg.Tag=='i')return true;
            return false;
        }
        public static string SanitizeCommand(string command)
        {
            var text=new System.Text.StringBuilder();
            foreach(char c in command??"") {if(text.Length==32)break;text.Append(c>=' ' && c<='~' && c!='/'?c:'_');}
            return text.ToString();
        }
        public static OscMessage Ack(string cueBase,string command,string result,bool playing,string detail="") =>
            new OscMessage(cueBase+"/ack",OscArg.String(SanitizeCommand(command)),OscArg.String(result),OscArg.Int(playing?1:0),OscArg.String(detail??""));
        /// <summary>Live-input gain: first float anywhere, else first int; must be finite 0..4.</summary>
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
