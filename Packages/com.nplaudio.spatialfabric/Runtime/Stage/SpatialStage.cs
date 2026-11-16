// Copyright (c) 2026 SpatialFabric Contributors. MIT.
using UnityEngine;
namespace NPLAudio.SpatialFabric
{
    public enum ListenerFollowMode { Off=0,PositionOnly=1,PositionAndOrientation=2 }
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class SpatialStage : SpatialIdentity
    {
        [Tooltip("Full physical size in canonical front/right/up meters (Unity Z/X/Y).")]
        public Vector3 dimensionsMeters = new Vector3(20, 15, 8);
        public bool flipFront, flipRight, flipUp;
        public bool followPositionAndOrientation; // Legacy compatibility; explicit mode wins when non-Off.
        public ListenerFollowMode followMode;
        public ListenerFollowMode EffectiveFollowMode=>followMode!=ListenerFollowMode.Off?followMode:followPositionAndOrientation?ListenerFollowMode.PositionAndOrientation:ListenerFollowMode.Off;
        [Tooltip("Actor/root transform, not implicitly a camera eye offset. Never moves the authored stage.")]
        public Transform listener;
        public bool allowLoadedAdditiveListener;
        public Vector3 Normalize(Vector3 world)
        {
            Vector3 p=transform.InverseTransformPoint(world);
            if(EffectiveFollowMode!=ListenerFollowMode.Off && listener!=null && (listener.gameObject.scene==gameObject.scene || allowLoadedAdditiveListener && listener.gameObject.scene.IsValid() && listener.gameObject.scene.isLoaded))
            {
                Quaternion rotation=EffectiveFollowMode==ListenerFollowMode.PositionOnly?transform.rotation:listener.rotation;
                p=Quaternion.Inverse(rotation)*(world-listener.position);Vector3 scale=transform.lossyScale;
                p=new Vector3(p.x/(Mathf.Abs(scale.x)<.0001f?.0001f:scale.x),p.y/(Mathf.Abs(scale.y)<.0001f?.0001f:scale.y),p.z/(Mathf.Abs(scale.z)<.0001f?.0001f:scale.z));
            }
            return new Vector3(Mathf.Clamp(p.z / Mathf.Max(.05f, dimensionsMeters.x * .5f), -1, 1) * (flipFront ? -1 : 1),
                Mathf.Clamp(p.x / Mathf.Max(.05f, dimensionsMeters.y * .5f), -1, 1) * (flipRight ? -1 : 1),
                Mathf.Clamp(p.y / Mathf.Max(.05f, dimensionsMeters.z * .5f), -1, 1) * (flipUp ? -1 : 1));
        }
        public Vector3 Meters(Vector3 normalized) => Vector3.Scale(normalized, dimensionsMeters * .5f);
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(.2f,.8f,1,.7f); Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(dimensionsMeters.y, dimensionsMeters.z, dimensionsMeters.x));
        }
    }
}
