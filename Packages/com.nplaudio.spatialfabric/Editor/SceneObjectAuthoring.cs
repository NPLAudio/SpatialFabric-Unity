// Loaded authoring-scene inventory and transactional component addition. No runtime I/O. MIT.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace NPLAudio.SpatialFabric.Editor
{
    public static class SceneObjectAuthoring
    {
        // Deliberate allowlist: no arbitrary scripts, dependencies, listeners or AudioSources.
        public static readonly IReadOnlyList<Type> ComponentTypes = Array.AsReadOnly(new[]{typeof(SpatialObject),typeof(PlaybackCue),typeof(LiveInputCue),typeof(SpatialPathPlayer),typeof(SpatialStage),typeof(SpatialFabricManager)});
        public static bool IsAuthoringObject(GameObject obj)
        {
            if(obj==null || EditorUtility.IsPersistent(obj) || StageUtility.GetStageHandle(obj)!=StageUtility.GetMainStageHandle())return false;
            var scene=obj.scene;
            if(!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene))return false;
            bool loaded=false;
            for(int i=0;i<SceneManager.sceneCount;++i)if(SceneManager.GetSceneAt(i)==scene){loaded=true;break;}
            if(!loaded)return false; // Excludes Prefab Mode and editor preview stages.
            for(var t=obj.transform;t!=null;t=t.parent)
                if((t.gameObject.hideFlags & HideFlags.DontSaveInEditor)!=0)return false;
            return true;
        }
        public static bool IsEditableObject(GameObject obj)
        {
            if(!IsAuthoringObject(obj))return false;
            for(var t=obj.transform;t!=null;t=t.parent)if((t.gameObject.hideFlags & HideFlags.NotEditable)!=0)return false;
            return !PrefabUtility.IsPartOfImmutablePrefab(obj);
        }
        public static GameObject[] Enumerate()
        {
            var result=new List<GameObject>();
            for(int i=0;i<SceneManager.sceneCount;++i)
            {
                var scene=SceneManager.GetSceneAt(i);
                if(!scene.isLoaded || EditorSceneManager.IsPreviewScene(scene))continue;
                foreach(var root in scene.GetRootGameObjects())
                    foreach(var t in root.GetComponentsInChildren<Transform>(true))
                        if(IsAuthoringObject(t.gameObject))result.Add(t.gameObject);
            }
            return result.ToArray();
        }
        // Read-only editor context discovery. Never creates a manager or binds objects.
        public static SpatialFabricManager ResolveManager(IEnumerable<SpatialFabricManager> candidates,SpatialFabricManager current,GameObject selection)
        {
            var managers=candidates.Where(m=>m!=null && IsAuthoringObject(m.gameObject)).Distinct().ToArray();
            var selectedManager=selection!=null?selection.GetComponent<SpatialFabricManager>():null;
            if(selectedManager!=null && managers.Contains(selectedManager))return selectedManager;
            if(current!=null && managers.Contains(current))return current;
            var active=SceneManager.GetActiveScene();var local=managers.Where(m=>m.gameObject.scene==active).ToArray();
            if(local.Length==1)return local[0];
            return managers.Length==1?managers[0]:null;
        }
        public static string HierarchyPath(GameObject obj)
        {
            string path=obj.name;
            for(var t=obj.transform.parent;t!=null;t=t.parent)path=t.name+"/"+path;
            return path;
        }
        public static void EnsureEditable()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Scene object authoring requires stopped Edit Mode (not compiling/importing).");
            foreach(var obj in Enumerate())
            {
                foreach(var manager in obj.GetComponents<SpatialFabricManager>())if(manager.IsListening)throw new InvalidOperationException("Stop scene OSC before authoring.");
                foreach(var cue in obj.GetComponents<PlaybackCue>())if(cue.IsPlaying || cue.ReplayPending || ActiveWorker(cue.WorkerState))throw new InvalidOperationException("Stop scene playback/replay before authoring.");
                foreach(var cue in obj.GetComponents<LiveInputCue>())if(cue.IsLive || ActiveWorker(cue.WorkerState))throw new InvalidOperationException("Stop scene live input before authoring.");
                foreach(var path in obj.GetComponents<SpatialPathPlayer>())if(path.IsPlaying)throw new InvalidOperationException("Stop scene path playback before authoring.");
            }
        }
        static bool ActiveWorker(Audio.AudioWorkerState state)=>state==Audio.AudioWorkerState.Starting || state==Audio.AudioWorkerState.Playing || state==Audio.AudioWorkerState.Draining;
        public static int AddComponents(IEnumerable<GameObject> selection,Type componentType)
        {
            EnsureEditable();
            if(!ComponentTypes.Contains(componentType))throw new ArgumentException("Choose a supported SpatialFabric scene component.");
            if(selection==null)throw new ArgumentNullException(nameof(selection));
            var targets=selection.Distinct().ToArray();
            if(targets.Length==0 || targets.Any(o=>!IsAuthoringObject(o)))throw new InvalidOperationException("Every target must be an editable GameObject in a loaded authoring scene.");
            // Validate the entire batch first, including targets already carrying the component.
            if(targets.Any(o=>!IsEditableObject(o)))
                throw new InvalidOperationException("The batch contains a locked/immutable prefab target.");
            var missing=targets.Where(o=>o.GetComponent(componentType)==null).ToArray();
            if(missing.Length==0)return 0;
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Add "+componentType.Name+" to scene objects");
            try
            {
                foreach(var obj in missing)
                {
                    var component=Undo.AddComponent(obj,componentType);
                    if(component==null)throw new InvalidOperationException("Unity refused "+componentType.Name+" on "+obj.name);
                    EditorUtility.SetDirty(component);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    EditorSceneManager.MarkSceneDirty(obj.scene);
                }
                Undo.CollapseUndoOperations(group);
                return missing.Length;
            }
            catch {Undo.RevertAllDownToGroup(group);throw;}
        }
        public static SerializedObject SerializedSelection(IEnumerable<GameObject> selection,Type componentType)
        {
            if(!ComponentTypes.Contains(componentType))throw new ArgumentException("Unsupported component type.");
            var components=selection.Where(IsAuthoringObject).SelectMany(o=>o.GetComponents(componentType))
                .Where(c=>c!=null && (c.hideFlags & (HideFlags.HideInInspector|HideFlags.NotEditable|HideFlags.DontSaveInEditor))==0).Cast<UnityEngine.Object>().ToArray();
            return components.Length==0?null:new SerializedObject(components);
        }
        // Startup opt-ins and internal identities are deliberately not editable here. The Inspector owns them.
        public static bool IsSafeProperty(string path)
        {
            return path!="m_Script" && path!="m_Enabled" && path!="persistentId" && path!="playOnStart" && path!="playOnEnable" && path!="enableInEditor" && path!="enableInPlay" && path!="runInBackground";
        }
        public static bool Apply(SerializedObject serialized)
        {
            EnsureEditable();
            if(serialized.targetObjects.Any(o=>!(o is Component c) || !ComponentTypes.Contains(c.GetType()) || !IsEditableObject(c.gameObject) || (c.hideFlags & HideFlags.NotEditable)!=0))
                throw new InvalidOperationException("Only unlocked SpatialFabric components in loaded authoring scenes can be edited.");
            bool changed=serialized.ApplyModifiedProperties();
            if(changed)foreach(var target in serialized.targetObjects)
            {
                var component=(Component)target;
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
            }
            return changed;
        }
    }
}
