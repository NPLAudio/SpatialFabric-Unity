// Copyright (c) 2026 SpatialFabric Contributors. MIT.
using System;
using UnityEngine;
namespace NPLAudio.SpatialFabric
{
    public abstract class SpatialIdentity : MonoBehaviour
    {
        [SerializeField, HideInInspector] string persistentId;
        public string Key => string.IsNullOrEmpty(persistentId) ? "unity-transient:" + UnityObjects.TransientId(this) : "unity:" + gameObject.scene.path + ":" + persistentId + (Application.isPlaying?":instance:"+UnityObjects.TransientId(this):"");
        protected virtual void Awake() { EnsureIdentity(); }
        protected virtual void OnValidate() { EnsureIdentity(); }
        void EnsureIdentity() { if (string.IsNullOrEmpty(persistentId)) persistentId = Guid.NewGuid().ToString("N"); }
        // Editor keys survive save/reload; live instance suffixes distinguish cloned active/inactive prefabs.
        // Reading a key never generates/repairs serialized identity. Runtime keys are scoped to one play session.
    }
}
