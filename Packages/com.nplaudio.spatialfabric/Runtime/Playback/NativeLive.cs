// Managed adapter for live input -> output. Explicit refresh/start only; no Unity Microphone/AudioSource. MIT.
using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using UnityEngine;
namespace NPLAudio.SpatialFabric.Audio
{
    public readonly struct LiveStatus
    {
        public readonly AudioWorkerState State;
        public readonly AudioEndReason Reason;
        public readonly int InputChannels, InputRate, OutputChannels, OutputRate, BufferFrames, LatencyFrames;
        public readonly bool ExclusiveActive;
        public readonly ulong StartedSequence, EndedSequence, CapturedFrames, RenderedFrames, Underruns, Overruns;
        public readonly float InputPeak;
        public readonly string Error;
        internal LiveStatus(NativeLive.Status s)
        {
            State=(AudioWorkerState)s.state;Reason=(AudioEndReason)s.reason;InputChannels=s.inChannels;InputRate=s.inRate;OutputChannels=s.outChannels;OutputRate=s.outRate;
            ExclusiveActive=s.exclusive!=0;BufferFrames=s.buffer;LatencyFrames=s.latency;StartedSequence=s.started;EndedSequence=s.ended;CapturedFrames=s.captured;RenderedFrames=s.rendered;
            Underruns=s.underruns;Overruns=s.overruns;InputPeak=float.IsNaN(s.peak)?0:Mathf.Clamp01(s.peak);
            int end=s.error==null?0:Array.IndexOf(s.error,(byte)0);Error=s.error==null?"":Encoding.UTF8.GetString(s.error,0,end<0?s.error.Length:end);
        }
    }
    public static class NativeLive
    {
        const string Library="SpatialFabricAudio";
        public static bool IsAvailable { get; private set; }
        public static string AvailabilityError { get; private set; }="Live input has not been probed.";
        public static AudioDeviceInfo[] CachedInputs { get; private set; }=Array.Empty<AudioDeviceInfo>();
        public static string InputEnumerationError { get; private set; }="Input devices have not been explicitly refreshed.";
        [StructLayout(LayoutKind.Sequential)] internal struct Status
        {
            public int state,reason,inChannels,inRate,outChannels,outRate,exclusive,buffer,latency;
            public ulong started,ended,captured,rendered,underruns,overruns;
            public float peak;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=1024)] public byte[] error;
        }
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern uint sf_live_abi_version();
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int sf_input_devices_json([Out] byte[] output,int capacity,[Out] byte[] error,int errorCapacity);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sf_live_create();
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int sf_live_start(IntPtr live,[In] byte[] inputId,int inputChannel,[In] byte[] outputId,int outputChannel,int exclusive,int bufferMs,float gain);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern void sf_live_gain(IntPtr live,float gain);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern void sf_live_status_get(IntPtr live,out Status status);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern void sf_live_stop(IntPtr live);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern void sf_live_destroy(IntPtr live);
        static string Utf8(byte[] bytes) {int end=Array.IndexOf(bytes,(byte)0);return Encoding.UTF8.GetString(bytes,0,end<0?bytes.Length:end);}
        static byte[] Id(string id)
        {
            if(id==null)id="";if(id.IndexOf('\0')>=0 || Encoding.UTF8.GetByteCount(id)>32767)throw new ArgumentException("Invalid device ID");
            return Encoding.UTF8.GetBytes(id+"\0");
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Probe()
        {
            IsAvailable=false;NativeAudio.Probe();
            if(!NativeAudio.IsAvailable) {AvailabilityError=NativeAudio.AvailabilityError;return;}
            try {if(sf_live_abi_version()!=1)throw new InvalidOperationException("Live input ABI mismatch");IsAvailable=true;AvailabilityError="";}
            catch(Exception e) when(e is DllNotFoundException || e is EntryPointNotFoundException || e is BadImageFormatException || e is InvalidOperationException) {AvailabilityError="This native plugin build has no live input: "+e.Message;}
        }
        /// <summary>Explicit capture-endpoint inventory. Opens no stream and requests no microphone access.</summary>
        public static AudioDeviceInfo[] RefreshInputs()
        {
            if(!IsAvailable)Probe();
            if(!IsAvailable) {InputEnumerationError=AvailabilityError;return CachedInputs;}
            var error=new byte[1024];int size=sf_input_devices_json(null,0,error,error.Length);
            for(int attempt=0;attempt<3 && size>0 && size<=1024*1024;++attempt)
            {
                var output=new byte[size];int required=sf_input_devices_json(output,output.Length,error,error.Length);
                if(required>output.Length) {size=required;continue;}
                if(required<1) {size=-1;break;}
                CachedInputs=JsonUtility.FromJson<DeviceInventory>(Utf8(output)).devices??Array.Empty<AudioDeviceInfo>();InputEnumerationError="";return CachedInputs;
            }
            InputEnumerationError=size<0?Utf8(error):"Input list changed/exceeded bounds; refresh again.";return CachedInputs;
        }
        [Serializable] sealed class DeviceInventory { public AudioDeviceInfo[] devices; }
        public sealed class Session : SafeHandleZeroOrMinusOneIsInvalid
        {
            internal Session(IntPtr value):base(true) {SetHandle(value);}
            protected override bool ReleaseHandle() {sf_live_destroy(handle);return true;}
            T With<T>(Func<IntPtr,T> action) {bool held=false;try{DangerousAddRef(ref held);return action(DangerousGetHandle());}finally{if(held)DangerousRelease();}}
            public LiveStatus Poll() => With(h=>{sf_live_status_get(h,out var status);return new LiveStatus(status);});
            public void Stop() {With(h=>{sf_live_stop(h);return 0;});}
            public void SetGain(float gain) {if(float.IsNaN(gain) || float.IsInfinity(gain) || gain<0 || gain>4)throw new ArgumentOutOfRangeException(nameof(gain));With(h=>{sf_live_gain(h,gain);return 0;});}
            /// <summary>A request, not an acknowledgement. Poll for the real worker state.</summary>
            public bool Start(string inputId,int inputChannel,string outputId,int outputChannel,bool exclusive,int bufferMs,float gain)
            {
                byte[] input=Id(inputId),output=Id(outputId);
                return With(h=>sf_live_start(h,input,inputChannel,output,outputChannel,exclusive?1:0,bufferMs,gain))!=0;
            }
        }
        public static Session Create()
        {
            if(!IsAvailable)Probe();if(!IsAvailable)throw new InvalidOperationException(AvailabilityError);
            IntPtr value=sf_live_create();if(value==IntPtr.Zero)throw new InvalidOperationException("Native live session allocation failed");return new Session(value);
        }
    }
}
