// Preflight the whole batch and cache only successfully sent frames. MIT.
using System;
using System.Collections.Generic;
using System.Text;
namespace NPLAudio.SpatialFabric.Osc
{
    public sealed class OutgoingBatch
    {
        string lastSignature;
        public void Reset(){lastSignature=null;}
        public int Send(OscEndpoint endpoint,string ip,int port,IList<OscMessage> messages,bool onlyOnChange)
        {
            OscEndpoint.Address(ip,port);
            if(messages==null || messages.Count>4096)throw new FormatException("OSC frame message limit exceeded");
            var signature=new StringBuilder(ip+":"+port+"|");
            foreach(var m in messages)signature.Append(Convert.ToBase64String(OscCodec.Encode(m))).Append('|');
            string current=signature.ToString();if(onlyOnChange && current==lastSignature)return 0;
            foreach(var m in messages)endpoint.Send(ip,port,m);
            lastSignature=current;return messages.Count;
        }
    }
}
