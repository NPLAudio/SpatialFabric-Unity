// Explicit mode selector also clears legacy compatibility flags when choosing Off. MIT.
using UnityEditor;
using UnityEngine;
namespace NPLAudio.SpatialFabric.Editor
{
    [CustomEditor(typeof(SpatialStage)),CanEditMultipleObjects]
    public sealed class SpatialStageEditor : UnityEditor.Editor
    {
        public static void DrawStage(SpatialStage stage)
        {
            var s=new SerializedObject(stage);s.Update();foreach(string name in new[]{"dimensionsMeters","flipFront","flipRight","flipUp","listener","allowLoadedAdditiveListener"})EditorGUILayout.PropertyField(s.FindProperty(name));s.ApplyModifiedProperties();
            var mode=(ListenerFollowMode)EditorGUILayout.EnumPopup("Virtual listener follow",stage.EffectiveFollowMode);
            if(mode!=stage.EffectiveFollowMode){Undo.RecordObject(stage,"Set virtual listener follow");stage.followMode=mode;stage.followPositionAndOrientation=false;PrefabUtility.RecordPrefabInstancePropertyModifications(stage);EditorUtility.SetDirty(stage);}
        }
        public override void OnInspectorGUI()
        {
            if(targets.Length==1){DrawStage((SpatialStage)target);return;}
            DrawDefaultInspector();EditorGUILayout.HelpBox("For legacy scenes, clear Follow Position And Orientation when explicitly disabling follow.",MessageType.None);
        }
    }
}
