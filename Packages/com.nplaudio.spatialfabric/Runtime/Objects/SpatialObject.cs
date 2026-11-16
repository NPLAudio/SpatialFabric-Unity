// Copyright (c) 2026 SpatialFabric Contributors. MIT.
using UnityEngine;
namespace NPLAudio.SpatialFabric
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class SpatialObject : SpatialIdentity
    {
        public int objectId = 1;
        public int admObjectIdOverride = -1;
        public Transform boundTransform;
        public string displayLabel="";
        public Transform Pose=>boundTransform!=null?boundTransform:transform;
        public string DisplayName=>string.IsNullOrEmpty(displayLabel)?name:displayLabel;
        public int AdmId=>admObjectIdOverride>=0?admObjectIdOverride:objectId;
        public System.Collections.Generic.List<Protocols.SpatialTargetOverride> targetOverrides = new System.Collections.Generic.List<Protocols.SpatialTargetOverride>();
        public int customObjectIdOverride = -1;
        [Range(0,2)] public float gain = 1;
        [Range(0,1)] public float width;
        public bool mute;
        public bool sendAdm = true, sendCustom = true;
        public System.Collections.Generic.List<Protocols.CustomField> customFields = new System.Collections.Generic.List<Protocols.CustomField>();
        public int CustomId => customObjectIdOverride >= 0 ? customObjectIdOverride : objectId;
    }
}
