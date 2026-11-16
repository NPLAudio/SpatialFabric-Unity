// Unity 6.6 retired InstanceID and the sort-mode overload; keep Unity 6.0 compatible.
using UnityEngine;
namespace NPLAudio.SpatialFabric
{
    public static class UnityObjects
    {
        public static string TransientId(Object item)
        {
#if UNITY_6000_6_OR_NEWER
            return item.GetEntityId().ToString();
#else
            return item.GetInstanceID().ToString();
#endif
        }
        public static T[] FindAll<T>(bool includeInactive = true) where T : Object
        {
            var inactive = includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
#if UNITY_6000_6_OR_NEWER
            return Object.FindObjectsByType<T>(inactive);
#else
            return Object.FindObjectsByType<T>(inactive,FindObjectsSortMode.None);
#endif
        }
    }
}
