// Explicit, undoable scene authoring; never performs transport/PCM/device operations. MIT.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace NPLAudio.SpatialFabric.Editor
{
    public static class SpatialAuthoring
    {
        public static SpatialFabricManager CreateManager()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before authoring");
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Create SpatialFabric stage/manager");
            var stage=new GameObject("SpatialFabric Stage").AddComponent<SpatialStage>();Undo.RegisterCreatedObjectUndo(stage.gameObject,"Create stage");
            var manager=new GameObject("SpatialFabric Manager").AddComponent<SpatialFabricManager>();Undo.RegisterCreatedObjectUndo(manager.gameObject,"Create manager");manager.stage=stage;EditorUtility.SetDirty(manager);Undo.CollapseUndoOperations(group);Selection.activeGameObject=manager.gameObject;return manager;
        }
        public static int AddBindings(SpatialFabricManager manager,IEnumerable<GameObject> selection)
        {
            RoutingCsv.EnsureIdle(manager);var objects=selection.Where(o=>o!=null && manager.ContainsScene(o.scene) && !EditorUtility.IsPersistent(o)).Distinct().ToArray();
            if(objects.Length==0)throw new InvalidOperationException("Select same-scene objects (not project assets)");
            var used=new HashSet<int>(manager.objects.Where(o=>o!=null).Select(o=>o.objectId));int next=1,added=0;
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Bind selected spatial objects");Undo.RegisterCompleteObjectUndo(manager,"Bind selected spatial objects");
            foreach(var obj in objects)
            {
                var binding=obj.GetComponent<SpatialObject>();if(binding==null){binding=Undo.AddComponent<SpatialObject>(obj);while(used.Contains(next))next=checked(next+1);binding.objectId=next;used.Add(next);}
                if(!manager.objects.Contains(binding)){manager.objects.Add(binding);++added;}
                PrefabUtility.RecordPrefabInstancePropertyModifications(binding);
            }
            EditorUtility.SetDirty(manager);PrefabUtility.RecordPrefabInstancePropertyModifications(manager);EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);Undo.CollapseUndoOperations(group);return added;
        }
        public static void EditBindings(SpatialFabricManager manager,IEnumerable<SpatialObject> selected,int? firstId=null,bool? mute=null)
        {
            RoutingCsv.EnsureIdle(manager);var targets=selected.Where(o=>o!=null && manager.objects.Contains(o) && manager.ContainsScene(o.gameObject.scene)).Distinct().ToArray();
            if(targets.Length==0)throw new InvalidOperationException("Select one or more bound rows");
            if(firstId.HasValue && (firstId.Value<1 || (long)firstId.Value+targets.Length-1>int.MaxValue))throw new ArgumentException("Invalid sequential ID range");
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Edit spatial binding selection");Undo.RegisterCompleteObjectUndo(targets.Cast<UnityEngine.Object>().ToArray(),"Edit spatial binding selection");
            for(int i=0;i<targets.Length;++i){var target=targets[i];if(firstId.HasValue)target.objectId=firstId.Value+i;if(mute.HasValue)target.mute=mute.Value;PrefabUtility.RecordPrefabInstancePropertyModifications(target);EditorUtility.SetDirty(target);}
            foreach(var scene in targets.Select(t=>t.gameObject.scene).Distinct())EditorSceneManager.MarkSceneDirty(scene);Undo.CollapseUndoOperations(group);
        }
        public static List<string> Validate(SpatialFabricManager manager)
        {
            var problems=new List<string>();if(manager==null){problems.Add("No manager");return problems;}
            if(manager.stage==null || !manager.ContainsScene(manager.stage.gameObject.scene))problems.Add("Stage is missing or belongs to another scene");
            var keys=new HashSet<string>(StringComparer.Ordinal);foreach(var output in manager.outputs)if(output==null || string.IsNullOrEmpty(output.key) || !keys.Add(output.key))problems.Add("Missing/duplicate output key");
            foreach(var binding in manager.objects)
            {
                if(binding==null){problems.Add("Missing binding reference");continue;}
                if(binding.objectId<1 || binding.AdmId<1 || binding.CustomId<1)problems.Add(binding.name+": IDs must be positive (override -1 inherits)");
                if(!manager.ContainsScene(binding.gameObject.scene) || !manager.ContainsScene(binding.Pose.gameObject.scene))problems.Add(binding.name+": cross-scene binding excluded from this world scope");
                if(manager.outputs.Count>0 && manager.stage!=null)foreach(var output in manager.outputs)try{if(output!=null)Protocols.SpatialOutputs.Id(binding,output);}catch(FormatException e){problems.Add(binding.name+": "+e.Message);}
            }
            foreach(var duplicate in manager.objects.Where(o=>o!=null).GroupBy(o=>o.Key).Where(g=>g.Count()>1))problems.Add("Duplicate binding identity/reference: "+duplicate.Key);
            return problems;
        }
    }
}
