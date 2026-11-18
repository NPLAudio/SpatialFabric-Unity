// Portable AudioLoom parity policies. No Unity, device or random-global dependencies. MIT.
using System;
using System.Security.Cryptography;
using System.Text;
namespace NPLAudio.SpatialFabric.Audio
{
    public static class PlaybackPolicy
    {
        static double Nonnegative(double value) => double.IsNaN(value) || double.IsInfinity(value) ? 0 : Math.Max(0,value);
        // xorshift32 is specified here rather than relying on engine/System.Random versions.
        // Zero seeds map to 1. One draw per eligible natural end, including equal bounds.
        public static double ReplayDelay(bool random,double fixedDelay,double min,double max,ref uint state)
        {
            if(!random)return Nonnegative(fixedDelay);
            min=Nonnegative(min);max=Nonnegative(max);if(min>max){double swap=min;min=max;max=swap;}
            if(state==0)state=1;
            unchecked {state^=state<<13;state^=state>>17;state^=state<<5;}
            return min+(max-min)*(state/4294967295.0);
        }
        public static bool RestartRoute(bool changed,AudioWorkerState state) => changed && (state==AudioWorkerState.Starting || state==AudioWorkerState.Playing || state==AudioWorkerState.Draining);
        // Hierarchy indices disambiguate duplicate labels and cloned prefabs; the scene token
        // scopes additive worlds. Explicit addresses remain the stable show-control contract.
        public static string DerivedBase(string sceneToken,string hierarchyIndices)
        {
            using(var hash=SHA256.Create())
            {
                byte[] bytes=hash.ComputeHash(Encoding.UTF8.GetBytes((sceneToken??"")+"\n"+(hierarchyIndices??"")));
                var text=new StringBuilder("/unity/playback/");foreach(byte b in bytes)text.Append(b.ToString("x2"));return text.ToString();
            }
        }
    }
}
