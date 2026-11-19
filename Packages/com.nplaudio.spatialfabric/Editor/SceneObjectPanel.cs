// Central scene grid within SpatialFabricWindow; uses the Inspector's serialized values. MIT.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace NPLAudio.SpatialFabric.Editor
{
    // Display-only snapshot. Mutation helpers must never use this cache for preflight.
    public sealed class SceneObjectViewCache
    {
        public const float RowHeight=20;
        public sealed class Row
        {
            public GameObject Object;
            public string Label,SearchPath,Summary;
            public int Components;
        }
        public Row[] Rows {get;private set;}=Array.Empty<Row>();
        public Row[] Visible {get;private set;}=Array.Empty<Row>();
        public readonly HashSet<GameObject> Objects=new HashSet<GameObject>();
        public readonly HashSet<GameObject> VisibleObjects=new HashSet<GameObject>();
        public readonly HashSet<GameObject> EditableObjects=new HashSet<GameObject>();
        public string Blocked {get;private set;}
        double refreshAt=double.NegativeInfinity;
        string lastSearch;
        int lastFilter=-1;
        bool invalid=true;
        public void Invalidate(){invalid=true;}
        public bool Refresh(double now,string search,int filter)
        {
            bool rebuild=invalid || now>=refreshAt;
            if(rebuild)
            {
                Rows=SceneObjectAuthoring.Enumerate().Select(obj=>
                {
                    int mask=0;var names=new List<string>();
                    for(int i=0;i<SceneObjectAuthoring.ComponentTypes.Count;++i)
                        if(obj.GetComponent(SceneObjectAuthoring.ComponentTypes[i])!=null){mask|=1<<i;names.Add(SceneObjectAuthoring.ComponentTypes[i].Name);}
                    string hierarchy=SceneObjectAuthoring.HierarchyPath(obj);
                    bool editable=SceneObjectAuthoring.IsEditableObject(obj);
                    return new Row{Object=obj,SearchPath=obj.scene.name+"/"+hierarchy,Label=obj.scene.name+" / "+hierarchy+(obj.activeInHierarchy?"":" [inactive]")+(editable?"":" [locked]"),
                        Summary=names.Count==0?"Unconfigured":string.Join(", ",names),Components=mask};
                }).ToArray();
                Objects.Clear();EditableObjects.Clear();
                foreach(var row in Rows){Objects.Add(row.Object);if(SceneObjectAuthoring.IsEditableObject(row.Object))EditableObjects.Add(row.Object);}
                Blocked=null;
                try{SceneObjectAuthoring.EnsureEditable();}catch(InvalidOperationException e){Blocked=e.Message;}
                invalid=false;refreshAt=now+.25;
            }
            if(!rebuild && search==lastSearch && filter==lastFilter)return false;
            Visible=Rows.Where(row=>(string.IsNullOrEmpty(search) || row.SearchPath.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0)
                && (filter==0 || filter==1 && row.Components==0 || filter>=2 && (row.Components & (1<<(filter-2)))!=0)).ToArray();
            VisibleObjects.Clear();foreach(var row in Visible)VisibleObjects.Add(row.Object);
            lastSearch=search;lastFilter=filter;return true;
        }
        // Exclusive end; partial rows included. Clamp stale scroll offsets after filtering.
        public static void VisibleRange(int count,float scroll,float height,out int first,out int end)
        {
            float top=Mathf.Clamp(scroll,0,Mathf.Max(0,count*RowHeight-height));
            first=Mathf.Clamp(Mathf.FloorToInt(top/RowHeight),0,count);
            end=Mathf.Clamp(Mathf.CeilToInt((top+Mathf.Max(0,height))/RowHeight),first,count);
        }
    }
    internal sealed class SceneObjectPanel
    {
        readonly HashSet<GameObject> selected=new HashSet<GameObject>();
        readonly HashSet<Type> expanded=new HashSet<Type>();
        string search="",error="";
        int filter,addType;
        bool followHierarchy=true,pendingHierarchySelection=true,expandSelection;
        Vector2 rowsScroll,propertiesScroll;
        GameObject pressedRow;
        bool pressedCheckbox;
        Rect lastViewport;
        // Last actual Repaint, used to verify visible UI rather than only inventory helpers.
        readonly Dictionary<GameObject,string> paintedRows=new Dictionary<GameObject,string>();
        readonly Dictionary<GameObject,Rect> paintedRowScreens=new Dictionary<GameObject,Rect>();
        readonly HashSet<string> paintedProperties=new HashSet<string>();
        readonly SceneObjectViewCache cache=new SceneObjectViewCache();
        readonly Dictionary<Type,UnityEngine.Object[]> propertyTargets=new Dictionary<Type,UnityEngine.Object[]>();
        static readonly string[] filters=new[]{"All objects","Unconfigured"}.Concat(SceneObjectAuthoring.ComponentTypes.Select(t=>t.Name)).ToArray();
        static readonly string[] choices=new[]{"Choose component to add…"}.Concat(SceneObjectAuthoring.ComponentTypes.Select(t=>t.Name)).ToArray();
        public void Invalidate(){cache.Invalidate();}
        public void HierarchySelectionChanged(){if(followHierarchy)pendingHierarchySelection=true;Invalidate();}
        void CaptureHierarchySelection()
        {
            if(!followHierarchy || !pendingHierarchySelection)return;
            pendingHierarchySelection=false;
            var current=new HashSet<GameObject>(Selection.gameObjects.Where(SceneObjectAuthoring.IsAuthoringObject));
            if(selected.SetEquals(current))return;
            selected.Clear();selected.UnionWith(current);expandSelection=true;
        }
        void SelectRow(GameObject obj,bool toggle)
        {
            followHierarchy=false;
            if(toggle){if(!selected.Add(obj))selected.Remove(obj);}
            else{selected.Clear();selected.Add(obj);}
            expandSelection=true;
        }
        void Run(Action action)
        {
            try{action();error="";}catch(Exception e){error=e.Message;}
            finally{Invalidate();}
            GUIUtility.ExitGUI();
        }
        public void Draw(float windowHeight)
        {
            if(Event.current.type==EventType.Repaint){paintedRows.Clear();paintedRowScreens.Clear();paintedProperties.Clear();}
            // Keep section membership stable for the whole Layout/input/Repaint cycle.
            // A destroyed captured component cannot be passed to SerializedObject.
            if(Event.current.type!=EventType.Layout && propertyTargets.Values.Any(targets=>targets.Any(target=>target==null)))
            {Invalidate();GUIUtility.ExitGUI();}
            EditorGUILayout.HelpBox("All loaded authoring scenes, including inactive/unconfigured objects. No assets, Prefab Mode or editor internals. No automatic I/O or binding.",MessageType.Info);
            using(new EditorGUILayout.HorizontalScope())
            {
                search=EditorGUILayout.TextField("Search scene / hierarchy",search);
                filter=EditorGUILayout.Popup(filter,filters,GUILayout.Width(170));
            }
            using(new EditorGUILayout.HorizontalScope())
            {
                bool follow=EditorGUILayout.ToggleLeft("Follow Hierarchy selection",followHierarchy,GUILayout.Width(210));
                if(follow!=followHierarchy){followHierarchy=follow;pendingHierarchySelection=follow;GUIUtility.ExitGUI();}
                if(GUILayout.Button("Refresh scene",GUILayout.Width(110))){Invalidate();GUIUtility.ExitGUI();}
            }
            // Only commit display snapshots on Layout: expiry must not change the
            // GUILayout tree between Layout and Repaint (e.g. the blocked help box).
            if(Event.current.type==EventType.Layout)
            {
                cache.Refresh(EditorApplication.timeSinceStartup,search,filter);
                CaptureHierarchySelection();
                selected.RemoveWhere(o=>o==null || !cache.Objects.Contains(o));
                var previousTypes=new HashSet<Type>(propertyTargets.Keys);
                propertyTargets.Clear();
                foreach(var type in SceneObjectAuthoring.ComponentTypes)
                    using(var serialized=SceneObjectAuthoring.SerializedSelection(selected,type))
                        if(serialized!=null)
                        {
                            propertyTargets.Add(type,serialized.targetObjects);
                            if(expandSelection || !previousTypes.Contains(type))expanded.Add(type);
                        }
                expandSelection=false;
            }
            var visible=cache.Visible;
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Select visible rows")){followHierarchy=false;foreach(var row in visible)if(row.Object!=null)selected.Add(row.Object);expandSelection=true;GUIUtility.ExitGUI();}
                if(GUILayout.Button("Clear rows")){followHierarchy=false;selected.Clear();GUIUtility.ExitGUI();}
                if(GUILayout.Button("Use Hierarchy selection")){followHierarchy=true;pendingHierarchySelection=true;GUIUtility.ExitGUI();}
                using(new EditorGUI.DisabledScope(selected.Count==0))
                {
                    if(GUILayout.Button("Select in Hierarchy"))Selection.objects=selected.Cast<UnityEngine.Object>().ToArray();
                    if(GUILayout.Button("Frame")){Selection.objects=selected.Cast<UnityEngine.Object>().ToArray();SceneView.lastActiveSceneView?.FrameSelected();}
                }
            }
            EditorGUILayout.LabelField($"{cache.Rows.Length} placed objects / {visible.Length} visible / {selected.Count} selected ({selected.Count(o=>!cache.VisibleObjects.Contains(o))} hidden by filter)");
            using(new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("",GUILayout.Width(20));GUILayout.Label("Scene / hierarchy (inactive marked)",GUILayout.MinWidth(230));GUILayout.Label("SpatialFabric components",GUILayout.Width(300));
            }
            float height=Mathf.Clamp((windowHeight-300)*.4f,80,210);
            var viewport=GUILayoutUtility.GetRect(0,height,GUILayout.ExpandWidth(true));
            if(Event.current.type!=EventType.Layout)lastViewport=viewport;
            else viewport=lastViewport; // GUILayout returns a dummy rect during Layout.
            // One control owns row interaction, independent of visible row count/order.
            int rowControl=GUIUtility.GetControlID("SpatialFabric scene rows".GetHashCode(),FocusType.Passive);
            float contentHeight=visible.Length*SceneObjectViewCache.RowHeight;
            // Always reserve the vertical scrollbar; horizontal scrolling is unnecessary.
            float width=Mathf.Max(0,viewport.width-16);
            rowsScroll.y=Mathf.Clamp(rowsScroll.y,0,Mathf.Max(0,contentHeight-viewport.height));
            bool mouseInRows=viewport.Contains(Event.current.mousePosition);
            rowsScroll=GUI.BeginScrollView(viewport,rowsScroll,new Rect(0,0,width,contentHeight),false,true);
            SceneObjectViewCache.VisibleRange(visible.Length,rowsScroll.y,viewport.height,out int first,out int end);
            for(int i=first;i<end;++i)
            {
                var row=visible[i];var obj=row.Object;if(obj==null)continue;
                float y=i*SceneObjectViewCache.RowHeight;
                var checkbox=new Rect(0,y,20,20);
                float summaryWidth=Mathf.Min(300,Mathf.Max(0,width-250));
                var label=new Rect(24,y,Mathf.Max(0,width-summaryWidth-24),20);
                var evt=Event.current;
                bool overCheckbox=checkbox.Contains(evt.mousePosition),overLabel=label.Contains(evt.mousePosition);
                if(evt.type==EventType.Repaint)EditorStyles.toggle.Draw(checkbox,GUIContent.none,false,false,selected.Contains(obj),false);
                GUI.Label(label,new GUIContent(row.Label,"Click to select and show live SpatialFabric properties; checkbox adds/removes a row"),EditorStyles.label);
                GUI.Label(new Rect(width-summaryWidth,y,summaryWidth,20),row.Summary);
                if(evt.type==EventType.Repaint){paintedRows[obj]=row.Label+" | "+row.Summary;paintedRowScreens[obj]=GUIUtility.GUIToScreenRect(label);}
                if(evt.type==EventType.MouseDown && evt.button==0 && mouseInRows && (overCheckbox || overLabel) && GUIUtility.hotControl==0)
                {
                    pressedRow=obj;pressedCheckbox=overCheckbox;GUIUtility.hotControl=rowControl;evt.Use();
                }
                if(evt.type==EventType.MouseUp && evt.button==0 && mouseInRows && GUIUtility.hotControl==rowControl && pressedRow==obj && (pressedCheckbox?overCheckbox:overLabel))
                {
                    GUIUtility.hotControl=0;pressedRow=null;evt.Use();
                    SelectRow(obj,pressedCheckbox);GUI.EndScrollView();GUIUtility.ExitGUI();
                }
            }
            // Release even if scrolling/filtering/deletion moved the pressed row away.
            if(Event.current.type==EventType.MouseUp && GUIUtility.hotControl==rowControl)
            {GUIUtility.hotControl=0;pressedRow=null;Event.current.Use();}
            GUI.EndScrollView();
            string blocked=cache.Blocked;
            if(blocked==null && selected.Any(o=>!cache.EditableObjects.Contains(o)))blocked="The selection contains locked/immutable objects. Remove them before editing.";
            if(blocked!=null)EditorGUILayout.HelpBox(blocked+" Grid and selection remain available; mutations are disabled.",MessageType.Warning);
            using(new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Property sections",EditorStyles.miniLabel);
                if(GUILayout.Button("Expand all",GUILayout.Width(90))){foreach(var type in propertyTargets.Keys)expanded.Add(type);GUIUtility.ExitGUI();}
                if(GUILayout.Button("Collapse all",GUILayout.Width(90))){expanded.Clear();GUIUtility.ExitGUI();}
            }
            using(new EditorGUI.DisabledScope(blocked!=null || selected.Count==0))
            {
                using(new EditorGUILayout.HorizontalScope())
                {
                    addType=EditorGUILayout.Popup("Add to selected",addType,choices);
                    using(new EditorGUI.DisabledScope(addType==0))if(GUILayout.Button("Add missing (one Undo)"))Run(()=>SceneObjectAuthoring.AddComponents(selected,SceneObjectAuthoring.ComponentTypes[addType-1]));
                }
                EditorGUILayout.LabelField("Shared serialized properties — mixed values show ‘—’",EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Edits affect ALL selected rows, including filtered-out rows, but only existing components. Startup opt-ins and live-input Start/consent remain in the Inspector.",MessageType.None);
                propertiesScroll=EditorGUILayout.BeginScrollView(propertiesScroll);
                if(propertyTargets.Count==0)
                    EditorGUILayout.HelpBox(selected.Count==0?"Select an object in the Hierarchy or click its scene row to show its current SpatialFabric properties.":"The selected objects have no editable SpatialFabric components yet. Choose a component above and click Add missing.",MessageType.Info);
                foreach(var type in SceneObjectAuthoring.ComponentTypes)
                {
                    if(!propertyTargets.TryGetValue(type,out var targets))continue;
                    using(var serialized=new SerializedObject(targets))
                    {
                        bool open=EditorGUILayout.Foldout(expanded.Contains(type),$"{type.Name} — {serialized.targetObjects.Length} components / {selected.Count} selected objects",true);
                        if(open)expanded.Add(type);else expanded.Remove(type);
                        if(!open)continue;
                        serialized.Update();
                        var property=serialized.GetIterator();bool children=true;
                        while(property.NextVisible(children))
                        {
                            children=false;
                            if(!SceneObjectAuthoring.IsSafeProperty(property.propertyPath))continue;
                            EditorGUILayout.PropertyField(property,true);
                            if(Event.current.type==EventType.Repaint)paintedProperties.Add(type.FullName+"."+property.propertyPath);
                        }
                        if(serialized.hasModifiedProperties)Run(()=>SceneObjectAuthoring.Apply(serialized));
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            if(!string.IsNullOrEmpty(error))EditorGUILayout.HelpBox(error,MessageType.Error);
        }
    }
}
