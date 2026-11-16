// Copyright (c) 2026 SpatialFabric Contributors. MIT.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NPLAudio.SpatialFabric.Osc
{
    public readonly struct OscArg
    {
        public readonly char Tag;
        public readonly object Value;
        public OscArg(char tag, object value) { Tag = tag; Value = value; }
        public static OscArg Float(float value) => new OscArg('f', value);
        public static OscArg Int(int value) => new OscArg('i', value);
        public static OscArg String(string value) => new OscArg('s', value);
    }
    public sealed class OscMessage
    {
        public readonly string Address;
        public readonly OscArg[] Arguments;
        public OscMessage(string address, params OscArg[] args) { Address = address; Arguments = args ?? Array.Empty<OscArg>(); }
    }
    public static class OscCodec
    {
        public const int MaxPacket = 65507, MaxArguments = 128, MaxMessages = 128;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public static bool ValidAddress(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 2 || value[0] != '/' || value.EndsWith("/") || value.Contains("//")) return false;
            foreach (char c in value) if (c < 33 || c > 126 || "#*,?[]{}".IndexOf(c) >= 0) return false;
            return true;
        }
        static void PutString(Stream output, string value)
        {
            if (value == null || value.IndexOf('\0') >= 0) throw new FormatException("Invalid OSC string");
            byte[] bytes = Utf8.GetBytes(value); output.Write(bytes, 0, bytes.Length); output.WriteByte(0);
            while (output.Length % 4 != 0) output.WriteByte(0);
        }
        static void PutNumber(Stream output, byte[] bytes)
        {
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes); output.Write(bytes, 0, bytes.Length);
        }
        public static byte[] Encode(OscMessage message)
        {
            if (message == null || !ValidAddress(message.Address) || message.Arguments.Length > MaxArguments) throw new FormatException("Invalid OSC message");
            using (var output = new MemoryStream())
            {
                PutString(output, message.Address); var tags = new StringBuilder(",");
                foreach (var a in message.Arguments) tags.Append(a.Tag);
                PutString(output, tags.ToString());
                foreach (var a in message.Arguments)
                {
                    switch (a.Tag)
                    {
                        case 'i': if (!(a.Value is int)) throw new FormatException("Expected int32"); PutNumber(output, BitConverter.GetBytes((int)a.Value)); break;
                        case 'f':
                            if (!(a.Value is float f) || float.IsNaN(f) || float.IsInfinity(f)) throw new FormatException("Expected finite float32");
                            PutNumber(output, BitConverter.GetBytes(f)); break;
                        case 's': PutString(output, a.Value as string); break;
                        case 'T': case 'F': case 'N': break;
                        default: throw new FormatException("Unsupported outgoing tag");
                    }
                }
                if (output.Length > MaxPacket) throw new FormatException("Datagram too large");
                return output.ToArray();
            }
        }
        public static List<OscMessage> Decode(byte[] packet)
        {
            if (packet == null || packet.Length < 8 || packet.Length > MaxPacket || packet.Length % 4 != 0) throw new FormatException("Invalid OSC size");
            var messages = new List<OscMessage>(); Parse(packet, 0, packet.Length, 0, messages); return messages;
        }
        static void Parse(byte[] data, int begin, int end, int depth, List<OscMessage> result)
        {
            if (depth > 8 || end - begin < 8 || (end - begin) % 4 != 0) throw new FormatException("Invalid/deep packet");
            int pos = begin;
            string address = ReadString(data, ref pos, end);
            if (address == "#bundle")
            {
                if (pos + 8 > end) throw new FormatException("Truncated bundle"); pos += 8; // Timetags intentionally immediate.
                while (pos < end)
                {
                    int length = (int)ReadNumber(data, ref pos, end, 'i');
                    if (length <= 0 || length > end - pos) throw new FormatException("Invalid bundle element");
                    Parse(data, pos, pos + length, depth + 1, result); pos += length;
                }
                return;
            }
            string tags = ReadString(data, ref pos, end);
            if (!ValidAddress(address) || !tags.StartsWith(",") || tags.Length > MaxArguments + 1) throw new FormatException("Invalid address/tags");
            var args = new List<OscArg>();
            foreach (char tag in tags.Substring(1))
            {
                object value;
                switch (tag)
                {
                    case 'i': case 'f': case 'h': case 'd': value = ReadNumber(data, ref pos, end, tag); break;
                    case 's': value = ReadString(data, ref pos, end); break;
                    case 'T': value = true; break;
                    case 'F': value = false; break;
                    case 'N': value = null; break;
                    case 'b':
                        int length = (int)ReadNumber(data, ref pos, end, 'i');
                        if (length < 0 || length > end - pos) throw new FormatException("Invalid blob size");
                        var blob = new byte[length]; Buffer.BlockCopy(data, pos, blob, 0, length); pos += length;
                        while ((pos - begin) % 4 != 0) { if (pos >= end || data[pos++] != 0) throw new FormatException("Invalid blob padding"); }
                        value = blob; break;
                    default: throw new FormatException("Unsupported incoming tag");
                }
                args.Add(new OscArg(tag, value));
            }
            if (pos != end || result.Count >= MaxMessages) throw new FormatException("Trailing bytes/message limit");
            result.Add(new OscMessage(address, args.ToArray()));
        }
        static string ReadString(byte[] data, ref int pos, int end)
        {
            int first = pos; while (pos < end && data[pos] != 0) ++pos;
            if (pos >= end) throw new FormatException("Unterminated string");
            string value = Utf8.GetString(data, first, pos - first); ++pos;
            while (pos % 4 != 0) { if (pos >= end || data[pos++] != 0) throw new FormatException("Invalid string padding"); }
            return value;
        }
        static object ReadNumber(byte[] data, ref int pos, int end, char tag)
        {
            int size = tag == 'h' || tag == 'd' ? 8 : 4;
            if (size > end - pos) throw new FormatException("Truncated numeric argument");
            var bytes = new byte[size]; Buffer.BlockCopy(data, pos, bytes, 0, size); pos += size;
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            switch (tag)
            {
                case 'i': return BitConverter.ToInt32(bytes, 0);
                case 'h': return BitConverter.ToInt64(bytes, 0);
                case 'f': float f = BitConverter.ToSingle(bytes, 0); if (float.IsNaN(f) || float.IsInfinity(f)) throw new FormatException("Non-finite float"); return f;
                default: double d = BitConverter.ToDouble(bytes, 0); if (double.IsNaN(d) || double.IsInfinity(d)) throw new FormatException("Non-finite double"); return d;
            }
        }
    }
}
