// Original scene animation; visual OSC objects never collide with the visitor. MIT.
using UnityEngine;
namespace NPLAudio.SpatialFabric.Example
{
    public sealed class RoomMotion : MonoBehaviour
    {
        public Vector3 origin,amplitude=new Vector3(2,.5f,2);
        public float frequency=.5f,phase;
        void Update(){float t=Time.time*frequency+phase;transform.position=origin+new Vector3(Mathf.Sin(t)*amplitude.x,Mathf.Sin(t*.7f)*amplitude.y,Mathf.Cos(t)*amplitude.z);}
    }
}
