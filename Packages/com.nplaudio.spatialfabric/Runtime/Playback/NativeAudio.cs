// Managed C ABI adapter. Queries read cached state only; no device enumeration on property access. MIT.
using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using UnityEngine;
namespace NPLAudio.SpatialFabric.Audio
{
    [Serializable] public sealed class AudioDeviceInfo { public string id,name; public int channels,sample_rate; public bool is_default; }
    [Serializable] sealed class DeviceInventory { public AudioDeviceInfo[] devices; }
    public readonly struct AudioStatus
    {
        public readonly AudioWorkerState State;
        public readonly AudioEndReason Reason;
        public readonly int OutputChannels,OutputRate,BufferFrames;
        public readonly bool ExclusiveActive;
        public readonly ulong StartedSequence,EndedSequence,RenderedFrames;
        public readonly string Error;
        internal AudioStatus(NativeAudio.Status s)
        {
            State=(AudioWorkerState)s.state;Reason=(AudioEndReason)s.reason;OutputChannels=s.channels;OutputRate=s.rate;BufferFrames=s.frames;ExclusiveActive=s.exclusive!=0;
            StartedSequence=s.started;EndedSequence=s.ended;RenderedFrames=s.rendered;
            int end=s.error==null?0:Array.IndexOf(s.error,(byte)0);Error=s.error==null?"":Encoding.UTF8.GetString(s.error,0,end<0?s.error.Length:end);
        }
    }
    public static class NativeAudio
    {
        const string Library="SpatialFabricAudio";
        public static bool IsAvailable { get; private set; }
        public static string AvailabilityError { get; private set; }="Native backend has not been probed. Build/install the Windows x64 plugin.";
        public static AudioDeviceInfo[] CachedDevices { get; private set; }=Array.Empty<AudioDeviceInfo>();
        public static string DeviceEnumerationError { get; private set; }="Devices have not been explicitly refreshed.";
        [StructLayout(LayoutKind.Sequential)] internal struct Status
        {
            public int state,reason,channels,rate,exclusive,frames;
            public ulong started,ended,rendered;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=1024)] public byte[] error;
        }
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern uint sf_audio_abi_version();
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int sf_devices_json([Out] byte[] output,int capacity,[Out] byte[] error,int errorCapacity);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sf_buffer_create([In] float[] samples,long count,int channels,int rate,[Out] byte[] error,int capacity);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern void sf_buffer_destroy(IntPtr buffer);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int sf_buffer_render(IntPtr buffer,[Out] float[] output,int frames,int channels,int rate,int selected,int loop,ref double position);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sf_player_create();
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int sf_player_start(IntPtr player,IntPtr buffer,[In] byte[] deviceId,int selected,int exclusive,int bufferMs,int loop);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern void sf_player_loop(IntPtr player,int loop);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern void sf_player_status(IntPtr player,out Status status);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern void sf_player_stop(IntPtr player);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern void sf_player_destroy(IntPtr player);
        static T WithHandle<T>(SafeHandle handle,Func<IntPtr,T> action)
        {
            if(handle==null)throw new ArgumentNullException(nameof(handle));bool held=false;
            try{handle.DangerousAddRef(ref held);return action(handle.DangerousGetHandle());}finally{if(held)handle.DangerousRelease();}
        }
        static string Utf8(byte[] bytes) {int end=Array.IndexOf(bytes,(byte)0);return Encoding.UTF8.GetString(bytes,0,end<0?bytes.Length:end);}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Probe()
        {
            IsAvailable=false;
            if ((Application.platform!=RuntimePlatform.WindowsEditor && Application.platform!=RuntimePlatform.WindowsPlayer && Application.platform!=RuntimePlatform.OSXEditor && Application.platform!=RuntimePlatform.OSXPlayer) || IntPtr.Size!=8) {AvailabilityError="Direct-device backend supports Windows x64 / macOS 64-bit with a matching native library; this platform is unsupported.";return;}
            try {if(sf_audio_abi_version()!=1)throw new InvalidOperationException("Native audio ABI mismatch");IsAvailable=true;AvailabilityError="";}
            catch(Exception e) when(e is DllNotFoundException || e is EntryPointNotFoundException || e is BadImageFormatException || e is InvalidOperationException) {AvailabilityError=e.Message;}
        }
        public static AudioDeviceInfo[] RefreshDevices()
        {
            if(!IsAvailable) Probe();
            if(!IsAvailable) {DeviceEnumerationError=AvailabilityError;return CachedDevices;}
            var error=new byte[1024];int size=sf_devices_json(null,0,error,error.Length);
            for(int attempt=0;attempt<3 && size>0 && size<=1024*1024;++attempt)
            {
                var output=new byte[size];int required=sf_devices_json(output,output.Length,error,error.Length);
                if(required>output.Length) {size=required;continue;}
                if(required<1) {size=-1;break;}
                CachedDevices=JsonUtility.FromJson<DeviceInventory>(Utf8(output)).devices??Array.Empty<AudioDeviceInfo>();DeviceEnumerationError="";return CachedDevices;
            }
            DeviceEnumerationError=size<0?Utf8(error):"Device list changed/exceeded bounds; refresh again.";return CachedDevices;
        }
        public sealed class Buffer : SafeHandleZeroOrMinusOneIsInvalid
        {
            internal Buffer(IntPtr value):base(true) {SetHandle(value);}
            protected override bool ReleaseHandle() {sf_buffer_destroy(handle);return true;}
        }
        public sealed class Player : SafeHandleZeroOrMinusOneIsInvalid
        {
            internal Player(IntPtr value):base(true) {SetHandle(value);}
            protected override bool ReleaseHandle() {sf_player_destroy(handle);return true;}
            public AudioStatus Poll() => WithHandle(this,h=>{sf_player_status(h,out var status);return new AudioStatus(status);});
            public void Stop() {WithHandle(this,h=>{sf_player_stop(h);return 0;});}
            public void SetLoop(bool loop) {WithHandle(this,h=>{sf_player_loop(h,loop?1:0);return 0;});}
            public bool Start(Buffer buffer,string deviceId,int selected,bool exclusive,int bufferMs,bool loop)
            {
                if (deviceId==null) deviceId="";
                if(deviceId.IndexOf('\0')>=0 || Encoding.UTF8.GetByteCount(deviceId)>32767) throw new ArgumentException("Invalid device ID");
                byte[] id=Encoding.UTF8.GetBytes(deviceId+"\0");
                return WithHandle(this,p=>WithHandle(buffer,b=>sf_player_start(p,b,id,selected,exclusive?1:0,bufferMs,loop?1:0)))!=0;
            }
        }
        public static Buffer Prepare(PcmData pcm)
        {
            if(!IsAvailable) Probe();if(!IsAvailable)throw new InvalidOperationException(AvailabilityError);
            if(pcm==null || pcm.Samples==null)throw new ArgumentException("No prepared PCM");var error=new byte[1024];
            IntPtr value=sf_buffer_create(pcm.Samples,pcm.Samples.LongLength,pcm.Channels,pcm.SampleRate,error,error.Length);
            if(value==IntPtr.Zero)throw new InvalidOperationException(Utf8(error));return new Buffer(value);
        }
        public static Player CreatePlayer()
        {
            if(!IsAvailable)Probe();if(!IsAvailable)throw new InvalidOperationException(AvailabilityError);
            IntPtr value=sf_player_create();if(value==IntPtr.Zero)throw new InvalidOperationException("Native player allocation failed");return new Player(value);
        }
        public static float[] RenderOffline(Buffer buffer,int frames,int channels,int rate,int selected,bool loop,ref double position,out bool ended)
        {
            if(buffer==null || frames<0 || channels<1 || (long)frames*channels>WavDecoder.MaxSamples)throw new ArgumentException("Invalid offline dimensions");
            var samples=new float[frames*channels];int result;bool held=false;
            try{buffer.DangerousAddRef(ref held);result=sf_buffer_render(buffer.DangerousGetHandle(),samples,frames,channels,rate,selected,loop?1:0,ref position);}finally{if(held)buffer.DangerousRelease();}
            if(result<0)throw new ArgumentException("Invalid native render request");ended=result==1;return samples;
        }
    }
}
