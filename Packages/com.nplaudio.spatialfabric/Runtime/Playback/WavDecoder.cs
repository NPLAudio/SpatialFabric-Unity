// Bounded RIFF/WAVE PCM reader; independent of Unity and its mixer/import settings. MIT.
using System;
using System.IO;
namespace NPLAudio.SpatialFabric.Audio
{
    public sealed class PcmData
    {
        public readonly float[] Samples;
        public readonly int Channels, SampleRate;
        public PcmData(float[] samples,int channels,int rate) { Samples = samples; Channels = channels; SampleRate = rate; }
    }
    public static class WavDecoder
    {
        public const int MaxSamples = 64*1024*1024;
        static uint Four(string s) => (uint)s[0] | (uint)s[1]<<8 | (uint)s[2]<<16 | (uint)s[3]<<24;
        public static PcmData Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12 || bytes.Length > 512*1024*1024) throw new FormatException("Invalid/bounded WAV byte size");
            using (var stream = new MemoryStream(bytes,false)) using (var r = new BinaryReader(stream))
            {
                if (r.ReadUInt32() != Four("RIFF")) throw new FormatException("Only little-endian RIFF/WAVE is supported");
                long end = (long)r.ReadUInt32()+8;
                if (end > bytes.Length || end < 12 || r.ReadUInt32() != Four("WAVE")) throw new FormatException("Invalid RIFF bounds/type");
                int channels = 0,rate = 0,bits = 0,valid = 0,align = 0,format = 0; long data = -1,size = 0; bool haveFormat = false;
                while (stream.Position < end)
                {
                    if (end-stream.Position < 8) throw new FormatException("Truncated WAV chunk header");
                    uint tag = r.ReadUInt32(),length = r.ReadUInt32(); long next = stream.Position+length;
                    if (next > end || next+(length&1) > end) throw new FormatException("WAV chunk/padding exceeds RIFF bounds");
                    if (tag == Four("fmt "))
                    {
                        if (haveFormat || length < 16) throw new FormatException("Duplicate/truncated WAV format");
                        haveFormat = true; format = r.ReadUInt16(); channels = r.ReadUInt16(); uint unsignedRate = r.ReadUInt32(); if(unsignedRate > 384000)throw new FormatException("WAV sample rate exceeds bounds"); rate = (int)unsignedRate;
                        uint byteRate = r.ReadUInt32(); align = r.ReadUInt16(); bits = valid = r.ReadUInt16();
                        if (format == 0xfffe)
                        {
                            if (length < 40) throw new FormatException("Invalid extensible WAV format");
                            int extra = r.ReadUInt16(); if(extra < 22 || extra > length-18)throw new FormatException("Invalid extensible WAV extension length");
                            valid = r.ReadUInt16(); r.ReadUInt32(); var guid = new Guid(r.ReadBytes(16));
                            if (guid == new Guid("00000001-0000-0010-8000-00aa00389b71")) format = 1;
                            else if (guid == new Guid("00000003-0000-0010-8000-00aa00389b71")) format = 3;
                            else throw new FormatException("Unsupported extensible WAV subtype");
                        }
                        if (channels < 1 || channels > 64 || rate < 8000 || rate > 384000 || bits%8 != 0 || valid < 1 || valid > bits ||
                            (format == 1 ? bits != 8 && bits != 16 && bits != 24 && bits != 32 : format != 3 || bits != 32 && bits != 64) ||
                            (format == 3 && valid != bits) || align != channels*(bits/8) || byteRate != (uint)((long)rate*align)) throw new FormatException("Unsupported/inconsistent WAV format");
                    }
                    else if (tag == Four("data"))
                    {
                        if (data >= 0) throw new FormatException("Duplicate WAV data chunk");
                        data = stream.Position; size = length;
                    }
                    stream.Position = next+(length&1);
                }
                if (!haveFormat || data < 0 || size < align || size%align != 0 || size/(bits/8) > MaxSamples) throw new FormatException("Missing/incomplete WAV PCM or sample limit exceeded");
                int count = checked((int)(size/(bits/8))); var samples = new float[count]; stream.Position = data;
                for (int i = 0; i < count; ++i)
                {
                    double value;
                    if (format == 3) value = bits == 32 ? r.ReadSingle() : r.ReadDouble();
                    else if (bits == 8) value = ((r.ReadByte()>>(8-valid))-(1<<(valid-1)))/Math.Pow(2,valid-1);
                    else
                    {
                        int word;
                        if (bits == 16) word = r.ReadInt16();
                        else if (bits == 24) { word = r.ReadByte() | r.ReadByte()<<8 | r.ReadByte()<<16; if ((word&0x800000) != 0) word |= unchecked((int)0xff000000); }
                        else word = r.ReadInt32();
                        word >>= bits-valid; value = word/Math.Pow(2,valid-1);
                    }
                    if (double.IsNaN(value) || double.IsInfinity(value)) throw new FormatException("Non-finite WAV sample");
                    samples[i] = (float)Math.Max(-1,Math.Min(1,value));
                }
                return new PcmData(samples,channels,rate);
            }
        }
    }
}
