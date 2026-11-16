// Original SpatialFabric example; MIT.
using UnityEngine;
namespace NPLAudio.SpatialFabric.Example
{
    public sealed class ShowcaseMotion : MonoBehaviour
    {
        Vector3 origin;
        void Start() { origin = transform.position; }
        void Update() { float t = Time.time * .5f; transform.position = origin + new Vector3(Mathf.Sin(t)*2,Mathf.Sin(t*.7f)*.5f,Mathf.Cos(t)*2); }
    }
}
