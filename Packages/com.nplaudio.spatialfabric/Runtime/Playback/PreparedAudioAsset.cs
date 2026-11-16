// Original SpatialFabric asset; no Unity AudioSource or runtime device access. MIT.
using System;
using UnityEngine;
using NPLAudio.SpatialFabric.Audio;
namespace NPLAudio.SpatialFabric
{
    [CreateAssetMenu(menuName="SpatialFabric/Prepared PCM",fileName="PreparedPCM")]
    public sealed class PreparedAudioAsset : ScriptableObject
    {
        [SerializeField,HideInInspector] float[] samples = Array.Empty<float>();
        [SerializeField] int channels, sampleRate;
        [SerializeField] string sourceName, revision;
        public int Channels => channels;
        public int SampleRate => sampleRate;
        public int SampleCount => samples?.Length ?? 0;
        public double Duration => channels > 0 && sampleRate > 0 ? (double)SampleCount/channels/sampleRate : 0;
        public string SourceName => sourceName;
        public string Revision => revision;
        public void SetPcm(PcmData pcm,string name)
        {
            if (pcm == null || pcm.Samples == null || pcm.Channels < 1 || pcm.Channels > 64 || pcm.SampleRate < 8000 || pcm.SampleRate > 384000 || pcm.Samples.Length < pcm.Channels || pcm.Samples.Length > WavDecoder.MaxSamples || pcm.Samples.Length%pcm.Channels != 0) throw new ArgumentException("Invalid prepared PCM");
            foreach (float value in pcm.Samples) if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("Non-finite PCM");
            samples = (float[])pcm.Samples.Clone(); channels = pcm.Channels; sampleRate = pcm.SampleRate; sourceName = name ?? ""; revision = Guid.NewGuid().ToString("N");
        }
        public PcmData Snapshot() => new PcmData((float[])samples.Clone(),channels,sampleRate);
    }
}
