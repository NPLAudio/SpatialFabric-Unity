// Copyright (c) 2026 SpatialFabric Contributors. MIT.
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using NPLAudio.SpatialFabric.Osc;
namespace NPLAudio.SpatialFabric
{
    public sealed class SceneQueryResponder
    {
        readonly Dictionary<string,double> lastByIp = new Dictionary<string,double>();
        double lastGlobal = double.NegativeInfinity;
        public static List<string> Chunks(string text,int maximumBytes = 900)
        {
            if (maximumBytes < 4) throw new ArgumentException("Chunk size must fit a Unicode scalar");
            var chunks = new List<string>(); int start = 0, position = 0, bytes = 0;
            while (position < text.Length)
            {
                int length = char.IsHighSurrogate(text[position]) && position + 1 < text.Length && char.IsLowSurrogate(text[position+1]) ? 2 : 1;
                int size = Encoding.UTF8.GetByteCount(text.Substring(position,length));
                if (bytes + size > maximumBytes) { chunks.Add(text.Substring(start,position-start)); start = position; bytes = 0; }
                bytes += size; position += length;
            }
            if (position > start || chunks.Count == 0) chunks.Add(text.Substring(start));
            return chunks;
        }
        public bool Handle(SpatialFabricManager owner,OscEndpoint endpoint,ReceivedOsc received,double now)
        {
            var message = received.Message;
            if (message.Address != "/spatialfabric/scene/query") return false;
            if (!owner.enableSceneQueries || (!owner.allowRemoteSceneQueries && !IPAddress.IsLoopback(received.Peer.Address))) return true;
            var args = message.Arguments;
            if ((args.Length != 2 && args.Length != 4) || args[0].Tag != 's' || args[1].Tag != 'i') return true;
            string id = (string)args[0].Value;
            if (id.Length < 1 || id.Length > 64) return true;
            foreach (char c in id) if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-')) return true;
            int port = (int)args[1].Value,offset = 0,limit = 64;
            if (args.Length == 4)
            {
                if (args[2].Tag != 'i' || args[3].Tag != 'i') return true;
                offset = (int)args[2].Value; limit = (int)args[3].Value;
            }
            if (port < 1024 || port > 65535 || offset < 0 || offset > 16384 || limit < 1 || limit > 64) return true;
            string ip = received.Peer.Address.ToString();
            if (now - lastGlobal < .1 || lastByIp.TryGetValue(ip,out double last) && now - last < .25) return true;
            if (lastByIp.Count >= 64 && !lastByIp.ContainsKey(ip))
            {
                var expired = new List<string>(); foreach (var pair in lastByIp) if (now - pair.Value > 5) expired.Add(pair.Key);
                foreach (string key in expired) lastByIp.Remove(key);
                if (lastByIp.Count >= 64) return true;
            }
            lastGlobal = now; lastByIp[ip] = now;
            string json = SceneInventory.GetJson(owner,offset,limit); var chunks = Chunks(json);
            if (chunks.Count > 128) chunks = Chunks(SceneInventory.Failure("chunk_limit_reduce_page_size"));
            for (int index = 0; index < chunks.Count; ++index)
                endpoint.Send(ip,port,new OscMessage("/spatialfabric/scene/response",OscArg.String(id),OscArg.Int(index),OscArg.Int(chunks.Count),OscArg.String(chunks[index])));
            return true;
        }
    }
}
