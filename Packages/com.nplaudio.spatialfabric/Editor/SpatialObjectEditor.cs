// Standard serialized multi-object Undo with explicit binding/target semantics. MIT.
using UnityEditor;
using UnityEngine;
namespace NPLAudio.SpatialFabric.Editor
{
    [CustomEditor(typeof(SpatialObject)),CanEditMultipleObjects]
    public sealed class SpatialObjectEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();EditorGUILayout.HelpBox("Pose defaults to this transform; bound transforms must remain in the manager's loaded scene. Per-output overrides match stable Output Key, -1 inherits the protocol ID. No inspector action starts networking or PCM.",MessageType.None);
            if(targets.Length==1){var obj=(SpatialObject)target;EditorGUILayout.LabelField("Current identity",obj.Key);EditorGUILayout.LabelField("Effective label",obj.DisplayName);}
        }
    }
}
