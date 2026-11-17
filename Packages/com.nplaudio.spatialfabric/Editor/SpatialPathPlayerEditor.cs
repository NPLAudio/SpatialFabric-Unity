// Copyright (c) 2026 SpatialFabric Contributors. MIT.
using UnityEditor;
using UnityEngine;
namespace NPLAudio.SpatialFabric.Editor
{
    /// <summary>Inspector with an edit-time preview slider. Only the previewed transform's position changes (with Undo).</summary>
    [CustomEditor(typeof(SpatialPathPlayer))]
    public sealed class SpatialPathPlayerEditor : UnityEditor.Editor
    {
        float preview;
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var player = (SpatialPathPlayer)target;
            if (player.asset != null && !player.asset.IsValid)
                EditorGUILayout.HelpBox("Path asset is invalid: " + player.asset.ProblemSummary(), MessageType.Error);
            if (!string.IsNullOrEmpty(player.LastError)) EditorGUILayout.HelpBox(player.LastError, MessageType.Warning);
            var track = player.ResolvedTrack;
            if (track == null) return;
            EditorGUILayout.Space();
            float duration = (float)player.Duration;
            EditorGUI.BeginChangeCheck();
            preview = EditorGUILayout.Slider("Preview time (s)", Mathf.Clamp(preview, 0, duration), 0, duration);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(player.MovedTransform, "Preview path position");
                player.Evaluate(preview);
            }
        }
    }
}
