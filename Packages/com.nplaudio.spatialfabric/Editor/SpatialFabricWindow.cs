// Functional operator/authoring panels; explicit actions, no startup I/O. MIT.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using NPLAudio.SpatialFabric.Audio;
namespace NPLAudio.SpatialFabric.Editor
{
    public sealed class SpatialFabricWindow : EditorWindow
    {
        SpatialFabricManager manager;
        Vector2 scroll;
        RadarView radarView;
        int tab=6,firstId=1;
        readonly SceneObjectPanel sceneObjects=new SceneObjectPanel();
        AudioRoutingPanel audioRouting;
        Rect scrollViewportScreen,windowViewportScreen;
        readonly HashSet<SpatialObject> selected=new HashSet<SpatialObject>();
        SpatialFabricManager[] sceneManagers=Array.Empty<SpatialFabricManager>();
        SpatialStage[] sceneStages=Array.Empty<SpatialStage>();
        SpatialObject[] sceneSpatialObjects=Array.Empty<SpatialObject>();
        PlaybackCue[] sceneCues=Array.Empty<PlaybackCue>();
        bool contextDirty=true,autoFindManager=true;
        bool radarExpanded,managerSettingsExpanded,networkOutputsExpanded;
        double contextRefreshAt;
        readonly Dictionary<UnityEngine.Object,bool> expandedTargets=new Dictionary<UnityEngine.Object,bool>();
        readonly Dictionary<UnityEngine.Object,Dictionary<string,bool>> propertyGroups=new Dictionary<UnityEngine.Object,Dictionary<string,bool>>();
        bool TargetFoldout(Component target,string label)
        {
            if(!expandedTargets.TryGetValue(target,out bool expanded))expanded=Selection.activeGameObject==target.gameObject;
            expanded=EditorGUILayout.Foldout(expanded,label,true);expandedTargets[target]=expanded;return expanded;
        }
        void Group(SerializedObject target,string title,bool initial,params string[] names)
        {
            if(!propertyGroups.TryGetValue(target.targetObject,out var groups)){groups=new Dictionary<string,bool>();propertyGroups[target.targetObject]=groups;}
            if(!groups.TryGetValue(title,out bool expanded))expanded=initial;
            expanded=EditorGUILayout.Foldout(expanded,title,true);groups[title]=expanded;
            if(expanded){EditorGUI.indentLevel++;Fields(target,names);EditorGUI.indentLevel--;}
        }
        readonly HashSet<UnityEngine.Object> paintedOperatorTargets=new HashSet<UnityEngine.Object>();
        void PaintTarget(UnityEngine.Object target){if(Event.current.type==EventType.Repaint && target!=null)paintedOperatorTargets.Add(target);}
        void RefreshContext()
        {
            double now=EditorApplication.timeSinceStartup;
            if(!contextDirty && now<contextRefreshAt)return;
            var objects=SceneObjectAuthoring.Enumerate();
            sceneManagers=objects.SelectMany(o=>o.GetComponents<SpatialFabricManager>()).ToArray();
            sceneStages=objects.SelectMany(o=>o.GetComponents<SpatialStage>()).ToArray();
            sceneSpatialObjects=objects.SelectMany(o=>o.GetComponents<SpatialObject>()).ToArray();
            sceneCues=objects.SelectMany(o=>o.GetComponents<PlaybackCue>()).ToArray();
            if(autoFindManager)manager=SceneObjectAuthoring.ResolveManager(sceneManagers,manager,Selection.activeGameObject);
            else if(manager!=null && !sceneManagers.Contains(manager))manager=null;
            foreach(var key in expandedTargets.Keys.Where(k=>k==null).ToArray())expandedTargets.Remove(key);
            foreach(var key in propertyGroups.Keys.Where(k=>k==null).ToArray())propertyGroups.Remove(key);
            contextDirty=false;contextRefreshAt=now+.25;
        }
        [MenuItem("Tools/SpatialFabric/Open Window")]
        public static void Open(){var window=GetWindow<SpatialFabricWindow>("SpatialFabric");window.minSize=new Vector2(680,480);window.contextDirty=true;window.Repaint();}
        void Action(Action action){try{action();}catch(Exception e){EditorUtility.DisplayDialog("SpatialFabric action refused",e.Message,"OK");}}
        static void Fields(SerializedObject target,params string[] names){foreach(string name in names){var property=target.FindProperty(name);if(property!=null)EditorGUILayout.PropertyField(property,true);}}
        void OnGUI()
        {
            windowViewportScreen=GUIUtility.GUIToScreenRect(new Rect(0,0,position.width,position.height));
            if(Event.current.type==EventType.Repaint)paintedOperatorTargets.Clear();
            if(Event.current.type==EventType.Layout)RefreshContext();
            else if(sceneStages.Any(s=>s==null) || sceneSpatialObjects.Any(o=>o==null) || sceneCues.Any(c=>c==null))
            {contextDirty=true;Repaint();GUIUtility.ExitGUI();}
            EditorGUILayout.LabelField("SpatialFabric — stages, routing and direct playback",EditorStyles.boldLabel);
            int nextTab=GUILayout.Toolbar(tab,new[]{"Stage","Objects","Outputs","Playback","CSV","Diagnostics","Scene Objects"});
            if(nextTab!=tab){tab=nextTab;Repaint();GUIUtility.ExitGUI();}
            if(tab==6){sceneObjects.Draw(position.height);return;}
            bool find=EditorGUILayout.Toggle("Auto-find scene manager",autoFindManager);
            if(find!=autoFindManager){autoFindManager=find;contextDirty=true;Repaint();GUIUtility.ExitGUI();}
            var chosen=(SpatialFabricManager)EditorGUILayout.ObjectField("Loaded-scene manager",manager,typeof(SpatialFabricManager),true);
            if(chosen!=manager)
            {
                if(chosen==null || sceneManagers.Contains(chosen)){manager=chosen;autoFindManager=false;}
                Repaint();GUIUtility.ExitGUI();
            }
            if(sceneManagers.Length>1)
            {
                var names=new[]{"Choose a scene manager…"}.Concat(sceneManagers.Select(m=>m==null?"Missing":m.gameObject.scene.name+" / "+SceneObjectAuthoring.HierarchyPath(m.gameObject))).ToArray();
                int index=Array.IndexOf(sceneManagers,manager)+1;
                int picked=EditorGUILayout.Popup("Scene managers",index,names);
                if(picked!=index){manager=picked==0?null:sceneManagers[picked-1];autoFindManager=false;Repaint();GUIUtility.ExitGUI();}
            }
            EditorGUILayout.LabelField("Live loaded-scene inventory",$"{sceneManagers.Length} managers / {sceneStages.Length} stages / {sceneSpatialObjects.Length} spatial objects / {sceneCues.Length} playback cues");
            if(manager==null)
            {
                EditorGUILayout.HelpBox(sceneManagers.Length==0?"No SpatialFabric manager in the loaded scenes. Existing stages, objects and playback cues are still shown below. Outputs, binding and CSV require a manager.":"Multiple scene managers found. Choose one above for outputs, binding and CSV; existing scene components remain visible below.",MessageType.Info);
                if(GUILayout.Button("Create stage + manager (Undo)")){Action(()=>{manager=SpatialAuthoring.CreateManager();contextDirty=true;});Repaint();GUIUtility.ExitGUI();}
            }
            else
            {
                using(new EditorGUILayout.HorizontalScope())
                {
                    using(new EditorGUI.DisabledScope(manager.IsListening))if(GUILayout.Button("Start configured OSC"))manager.StartNetworking();
                    using(new EditorGUI.DisabledScope(!manager.IsListening))if(GUILayout.Button("Stop local OSC"))manager.StopNetworking();
                    if(GUILayout.Button("Copy scene JSON"))EditorGUIUtility.systemCopyBuffer=SceneInventory.GetJson(manager);
                }
                EditorGUILayout.LabelField("Actual inputs",manager.IsListening?$"{manager.BoundSpatialIp}:{manager.BoundSpatialPort} / {manager.BoundPlaybackIp}:{manager.BoundPlaybackPort}":"Stopped");
            }
            if(tab==0 || tab==1 || tab==3)
            {
                using(new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Scene component sections",EditorStyles.miniLabel);
                    bool expand=GUILayout.Button("Expand all",GUILayout.Width(90));
                    bool collapse=GUILayout.Button("Collapse all",GUILayout.Width(90));
                    if(expand || collapse)
                    {
                        foreach(var target in sceneStages.Cast<UnityEngine.Object>().Concat(sceneSpatialObjects).Concat(sceneCues).Where(o=>o!=null))expandedTargets[target]=expand;
                        Repaint();GUIUtility.ExitGUI();
                    }
                }
            }
            scroll=EditorGUILayout.BeginScrollView(scroll);
            switch(tab){case 0:Stage();break;case 1:Objects();break;case 2:Outputs();break;case 3:Playback();break;case 4:if(manager!=null)Csv();else EditorGUILayout.HelpBox("Choose a loaded-scene manager for routing CSV.",MessageType.Info);break;case 5:if(manager!=null)Diagnostics();else EditorGUILayout.HelpBox("No manager selected; loaded scene inventory is shown above.",MessageType.Info);break;}
            EditorGUILayout.EndScrollView();
            if(Event.current.type==EventType.Repaint)scrollViewportScreen=GUIUtility.GUIToScreenRect(GUILayoutUtility.GetLastRect());
            EditorGUILayout.HelpBox("OSC is not PCM. Stop OSC does not stop audio. Closing this window never starts/stops remote playback. Input changes require Stop/Start.",MessageType.None);
        }
        void Stage()
        {
            if(manager!=null)
            {
                radarExpanded=EditorGUILayout.Foldout(radarExpanded,"Stage radar",true);
                if(radarExpanded)SpatialRadar.Draw(manager,ref radarView);
                managerSettingsExpanded=EditorGUILayout.Foldout(managerSettingsExpanded,"Manager stage / network settings",true);
                if(managerSettingsExpanded)
                {
                    var serialized=new SerializedObject(manager);serialized.Update();Fields(serialized,"stage","additionalScenePaths","enableInEditor","enableInPlay","runInBackground","listenIp","spatialInputPort","playbackInputPort","enableSceneQueries","allowRemoteSceneQueries");serialized.ApplyModifiedProperties();
                }
            }
            var stages=sceneStages.Concat(manager!=null && manager.stage!=null?new[]{manager.stage}:Array.Empty<SpatialStage>()).Where(s=>s!=null).Distinct().ToArray();
            if(stages.Length==0)EditorGUILayout.HelpBox("No SpatialStage component in the loaded scenes. Add one through Scene Objects; no stage is created implicitly.",MessageType.Info);
            foreach(var stage in stages)
            {
                if(!TargetFoldout(stage,stage.gameObject.scene.name+" / "+SceneObjectAuthoring.HierarchyPath(stage.gameObject)+(manager!=null && manager.stage==stage?" [assigned]":" [unassigned]")))continue;
                using(new EditorGUI.DisabledScope(!SceneObjectAuthoring.IsEditableObject(stage.gameObject)))SpatialStageEditor.DrawStage(stage);
                PaintTarget(stage);
            }
        }
        void Objects()
        {
            var bound=new HashSet<SpatialObject>(manager!=null?manager.objects.Where(o=>o!=null):Enumerable.Empty<SpatialObject>());
            var objects=sceneSpatialObjects.Concat(bound).Where(o=>o!=null && SceneObjectAuthoring.IsAuthoringObject(o.gameObject)).Distinct().ToArray();
            EditorGUILayout.HelpBox("All loaded-scene SpatialObject components are shown, including unbound objects. Browsing never enrolls them in a manager. Runtime routing still uses that manager's binding list and scene scope.",MessageType.Info);
            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUI.DisabledScope(manager==null))if(GUILayout.Button("Bind selected scene objects"))Action(()=>SpatialAuthoring.AddBindings(manager,selected.Count>0?selected.Where(o=>o!=null).Select(o=>o.gameObject):Selection.gameObjects));
                if(GUILayout.Button("Select all rows")){selected.Clear();foreach(var obj in objects)selected.Add(obj);}
                if(GUILayout.Button("Clear row selection"))selected.Clear();
            }
            using(new EditorGUI.DisabledScope(manager==null || !selected.Any(bound.Contains)))
            using(new EditorGUILayout.HorizontalScope())
            {
                firstId=EditorGUILayout.IntField("Sequential IDs from",firstId);
                if(GUILayout.Button("Assign to selected"))Action(()=>SpatialAuthoring.EditBindings(manager,selected,firstId));
                if(GUILayout.Button("Mute selected"))Action(()=>SpatialAuthoring.EditBindings(manager,selected,null,true));
                if(GUILayout.Button("Unmute"))Action(()=>SpatialAuthoring.EditBindings(manager,selected,null,false));
            }
            if(objects.Length==0)EditorGUILayout.HelpBox("No SpatialObject components in the loaded scenes. Use Scene Objects to explicitly add SpatialFabric functionality to ordinary objects.",MessageType.Info);
            foreach(var obj in objects)
            {
                using(new EditorGUILayout.HorizontalScope())
                {
                    bool check=GUILayout.Toggle(selected.Contains(obj),"",GUILayout.Width(20));if(check)selected.Add(obj);else selected.Remove(obj);
                    TargetFoldout(obj,obj.gameObject.scene.name+" / "+SceneObjectAuthoring.HierarchyPath(obj.gameObject)+(bound.Contains(obj)?" [bound]":" [unbound]"));
                    if(GUILayout.Button("Inspect",GUILayout.Width(70)))Selection.activeGameObject=obj.gameObject;
                }
                if(!expandedTargets[obj])continue;
                using(new EditorGUI.DisabledScope(!SceneObjectAuthoring.IsEditableObject(obj.gameObject)))
                {
                    var serialized=new SerializedObject(obj);serialized.Update();
                    Group(serialized,"Identity / protocols",true,"boundTransform","displayLabel","objectId","admObjectIdOverride","customObjectIdOverride","sendAdm","sendCustom");
                    Group(serialized,"Level / width / mute",true,"gain","width","mute");
                    Group(serialized,"Per-output overrides / custom fields",false,"targetOverrides","customFields");serialized.ApplyModifiedProperties();
                }
                PaintTarget(obj);
                if(manager!=null && manager.stage!=null)EditorGUILayout.LabelField("Current front/right/up",manager.stage.Normalize(obj.Pose.position).ToString("F3"));EditorGUILayout.Space(8);
            }
            if(manager!=null)
            {
                if(manager.objects.Any(o=>o==null))EditorGUILayout.HelpBox("Missing binding reference; edit the binding list below.",MessageType.Warning);
                var list=new SerializedObject(manager);list.Update();Fields(list,"objects");list.ApplyModifiedProperties();
            }
        }
        void Outputs()
        {
            if(audioRouting==null)audioRouting=new AudioRoutingPanel();
            audioRouting.Draw(sceneCues,AudioRoutingMatrixView.Intersection(scrollViewportScreen,windowViewportScreen));
            EditorGUILayout.Space(8);
            networkOutputsExpanded=EditorGUILayout.Foldout(networkOutputsExpanded,"OSC / network destinations (not audio channels)",true);
            if(!networkOutputsExpanded)return;
            if(manager==null){EditorGUILayout.HelpBox("Output destinations are stored on a SpatialFabricManager. Select a loaded manager above, or explicitly create one. No destinations are inferred or created from scene objects.",MessageType.Info);return;}
            PaintTarget(manager);
            EditorGUILayout.HelpBox("A nonempty Outputs list replaces legacy endpoints. Every destination has its own identity, protocol, IP/port, rate and change cache. Object rows can override IDs/fields by Output Key.",MessageType.Info);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Add ADM destination"))AddOutput(Protocols.SpatialProtocol.ADM);
                if(GUILayout.Button("Add Custom destination"))AddOutput(Protocols.SpatialProtocol.Custom);
            }
            var s=new SerializedObject(manager);s.Update();Fields(s,"outputs");EditorGUILayout.LabelField("Legacy fallback (only when Outputs is empty)",EditorStyles.boldLabel);
            Fields(s,"targetIp","sendRateHz","sendAdm","admPort","admCoordinateMode","admSendRateHz","admSendOnlyOnChange","sendCustom","customPort","customAddressTemplate","customProtocol","monitorPort");s.ApplyModifiedProperties();
        }
        void AddOutput(Protocols.SpatialProtocol protocol)
        {
            Action(()=>{RoutingCsv.EnsureIdle(manager);Undo.RecordObject(manager,"Add protocol destination");manager.outputs.Add(new Protocols.SpatialOutput{key=Guid.NewGuid().ToString("N"),label=protocol+" output",protocol=protocol,ip=manager.targetIp,port=protocol==Protocols.SpatialProtocol.ADM?manager.admPort:manager.customPort});EditorUtility.SetDirty(manager);});
        }
        void Playback()
        {
            if(audioRouting==null)audioRouting=new AudioRoutingPanel();
            if(!NativeAudio.IsAvailable)EditorGUILayout.HelpBox(NativeAudio.AvailabilityError,MessageType.Warning);
            using(new EditorGUILayout.HorizontalScope())
            {
                audioRouting.DrawRefreshButton();
                if(GUILayout.Button("Stop all scene cues + replay"))foreach(var cue in sceneCues.Where(c=>c!=null))cue.Stop();
                if(GUILayout.Button("Inspect all cues"))Selection.objects=sceneCues.Where(c=>c!=null).Cast<UnityEngine.Object>().ToArray();
            }
            audioRouting.DrawInventoryStatus();
            EditorGUILayout.HelpBox("Playback cues from all loaded authoring scenes are shown, including inactive objects. Opening this tab does not prepare PCM or play audio; OSC reachability still depends on manager world scope.",MessageType.Info);
            if(!sceneCues.Any(c=>c!=null))EditorGUILayout.HelpBox("No PlaybackCue components in the loaded scenes. Use Scene Objects to add playback to an existing object.",MessageType.Info);
            foreach(var cue in sceneCues.Where(c=>c!=null))
            {
                if(!TargetFoldout(cue,cue.gameObject.scene.name+" / "+SceneObjectAuthoring.HierarchyPath(cue.gameObject)+" — "+cue.WorkerState))continue;
                PaintTarget(cue);
                EditorGUILayout.LabelField("OSC base",cue.EffectiveOscBase);
                EditorGUILayout.LabelField(cue.name+" — "+cue.WorkerState+(cue.ReplayPending?" / replay pending":""),EditorStyles.boldLabel);
                var s=new SerializedObject(cue);s.Update();
                Group(s,"Source / OSC",true,"preparedAudio","clip","oscBase");
                Group(s,"Loop / replay / startup",false,"loop","autoReplay","playOnStart","replayDelaySeconds","randomReplayDelay","replayDelayMin","replayDelayMax","replayRandomSeed");
                Group(s,"Native stream options",false,"exclusiveRequested","bufferMsRequested");s.ApplyModifiedProperties();
                audioRouting.DrawRouteSelectors(cue);
                using(new EditorGUILayout.HorizontalScope())
                {
                    if(GUILayout.Button("Prepare PCM"))cue.Prepare();if(GUILayout.Button("Play physical output"))cue.TryPlay();if(GUILayout.Button("Stop + cancel replay"))cue.Stop();if(GUILayout.Button("Release PCM"))cue.ReleaseAudio();
                }
                EditorGUILayout.LabelField("Output mode / OS output latency",$"{cue.OutputMode} / "+(cue.OutputLatencyMilliseconds<0?"unavailable":cue.OutputLatencyMilliseconds.ToString("F2")+" ms (not acoustic)"));
                EditorGUILayout.LabelField("Observed output",$"{cue.ActualOutputChannels} ch / {cue.ActualOutputRate} Hz / {cue.ActualBufferFrames} frames / exclusive {cue.ExclusiveActive}");
                if(!string.IsNullOrEmpty(cue.LastError))EditorGUILayout.HelpBox(cue.LastError,MessageType.Warning);EditorGUILayout.Space(10);
            }
        }
        void Csv()
        {
            EditorGUILayout.HelpBox("Preview → confirm → one Undo. All scene OSC/audio/replay must be stopped. Playback v2 exports exact GUID:localFileId source references; v1 files remain compatible and preserve sources. Import never starts native PCM/devices/transport.",MessageType.Info);
            if(GUILayout.Button("Export spatial route CSV"))RoutingCsvActions.Export(manager,false);
            if(GUILayout.Button("Export playback + source references CSV (v2)"))RoutingCsvActions.Export(manager,true);
            if(GUILayout.Button("Preview spatial import"))RoutingCsvActions.Import(manager,false);
            if(GUILayout.Button("Preview playback import (v1/v2)"))RoutingCsvActions.Import(manager,true);
        }
        void Diagnostics()
        {
            EditorGUILayout.LabelField("Sent / received",$"{manager.Sent} / {manager.Received}");
            if(!string.IsNullOrEmpty(manager.LastError))EditorGUILayout.HelpBox("Last reported error: "+manager.LastError,MessageType.Warning);
            var issues=SpatialAuthoring.Validate(manager);if(issues.Count==0)EditorGUILayout.HelpBox("Binding/output references validate. This is not physical renderer/audio certification.",MessageType.Info);else foreach(string issue in issues)EditorGUILayout.HelpBox(issue,MessageType.Warning);
            if(GUILayout.Button("Select manager"))Selection.activeGameObject=manager.gameObject;
            if(GUILayout.Button("Repair duplicate scene identities (Undo)"))SceneIdentityTools.RepairScene(manager.gameObject.scene);
        }
        void SceneObjectsChanged(){contextDirty=true;sceneObjects.Invalidate();Repaint();}
        void OnSelectionChange(){contextDirty=true;sceneObjects.HierarchySelectionChanged();Repaint();}
        void OnEnable(){EditorApplication.hierarchyChanged+=SceneObjectsChanged;Undo.undoRedoPerformed+=SceneObjectsChanged;sceneObjects.HierarchySelectionChanged();}
        void OnDisable(){EditorApplication.hierarchyChanged-=SceneObjectsChanged;Undo.undoRedoPerformed-=SceneObjectsChanged;}
        void OnInspectorUpdate(){selected.RemoveWhere(o=>o==null);Repaint();}
    }
}
