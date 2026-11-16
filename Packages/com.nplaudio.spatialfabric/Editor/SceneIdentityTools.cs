// Explicit authoring action only; discovery never repairs or synthesizes identities.
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace NPLAudio.SpatialFabric.Editor
{
    public static class SceneIdentityTools
    {
        [MenuItem("Tools/SpatialFabric/Repair duplicate scene identities")]
        public static void RepairMenu() { UnityEngine.Debug.Log($"SpatialFabric repaired {Repair()} duplicate identities (Undo supported)."); }
        public static int Repair() => RepairItems(UnityObjects.FindAll<SpatialIdentity>());
        public static int RepairScene(UnityEngine.SceneManagement.Scene scene) => RepairItems(SceneInventory.InScene<SpatialIdentity>(scene));
        static int RepairItems(SpatialIdentity[] items)
        {
            int changed = 0;
            var groups = items.Where(item => item.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(item))
                .GroupBy(item => item.Key).Where(group => group.Count() > 1).ToArray();
            foreach (var group in groups)
            foreach (var item in group.Skip(1))
            {
                Undo.RecordObject(item,"Repair SpatialFabric identity");
                var serialized = new SerializedObject(item); serialized.Update();
                serialized.FindProperty("persistentId").stringValue = Guid.NewGuid().ToString("N");
                serialized.ApplyModifiedProperties(); EditorSceneManager.MarkSceneDirty(item.gameObject.scene); ++changed;
            }
            return changed;
        }
    }
}
