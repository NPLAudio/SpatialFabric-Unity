// Read-only authoring projection. Selection is explicit; no transform, network or audio mutation. MIT.
using UnityEditor;
using UnityEngine;
namespace NPLAudio.SpatialFabric.Editor
{
    public enum RadarView { Top=0,Front=1,Side=2,Isometric=3 }
    public static class SpatialRadar
    {
        sealed class Trail {public SpatialObject owner;public System.Collections.Generic.Queue<Vector3> points=new System.Collections.Generic.Queue<Vector3>();public double next;}
        static readonly System.Collections.Generic.Dictionary<string,Trail> trails=new System.Collections.Generic.Dictionary<string,Trail>();
        static SpatialFabricManager lastManager;
        static bool showTrails;
        public static Vector2 Project(Vector3 canonical,RadarView view)
        {
            switch(view){case RadarView.Top:return new Vector2(canonical.y,canonical.x);case RadarView.Front:return new Vector2(canonical.y,canonical.z);case RadarView.Side:return new Vector2(canonical.x,canonical.z);case RadarView.Isometric:return new Vector2((canonical.y-canonical.x)*.5f,(canonical.x+canonical.y)*.25f+canonical.z*.5f);default:throw new System.ArgumentException("Unknown radar view");}
        }
        public static void Draw(SpatialFabricManager manager,ref RadarView view)
        {
            view=(RadarView)GUILayout.Toolbar((int)view,new[]{"Top","Front","Side","Isometric"});
            showTrails=GUILayout.Toggle(showTrails,"Position history (view only, up to 120 samples / 256 objects)");
            if(lastManager!=manager){trails.Clear();lastManager=manager;}
            var removed=new System.Collections.Generic.List<string>();foreach(var pair in trails)if(pair.Value.owner==null || !manager.objects.Contains(pair.Value.owner))removed.Add(pair.Key);foreach(var key in removed)trails.Remove(key);
            if(manager.stage==null || !manager.ContainsScene(manager.stage.gameObject.scene)){EditorGUILayout.HelpBox("Assign a same-scene stage to view positions.",MessageType.None);return;}
            Rect area=GUILayoutUtility.GetRect(240,260,GUILayout.ExpandWidth(true));area=new Rect(area.x+10,area.y+10,area.width-20,area.height-20);
            if(Event.current.type==EventType.Repaint)
            {
                EditorGUI.DrawRect(area,new Color(.08f,.1f,.12f));Handles.BeginGUI();Handles.color=new Color(.3f,.35f,.4f);
                for(int i=0;i<=4;++i){float t=i/4f;Handles.DrawLine(new Vector3(area.x+t*area.width,area.y),new Vector3(area.x+t*area.width,area.yMax));Handles.DrawLine(new Vector3(area.x,area.y+t*area.height),new Vector3(area.xMax,area.y+t*area.height));}Handles.EndGUI();
            }
            foreach(var obj in manager.objects)
            {
                if(!manager.Resolves(obj))continue;
                Vector3 normalized=manager.stage.Normalize(obj.Pose.position);
                if(!Protocols.CustomProtocol.Finite(normalized.x) || !Protocols.CustomProtocol.Finite(normalized.y) || !Protocols.CustomProtocol.Finite(normalized.z))continue;
                Vector2 plane=Project(normalized,view);
                Vector2 point=new Vector2(area.x+(plane.x+1)*area.width*.5f,area.y+(1-plane.y)*area.height*.5f);
                if(showTrails)
                {
                    string key=UnityObjects.TransientId(obj);
                    if(!trails.TryGetValue(key,out var trail) && trails.Count<256){trail=new Trail{owner=obj};trails.Add(key,trail);}
                    if(trail!=null)
                    {
                        double now=EditorApplication.timeSinceStartup;if(now>=trail.next){trail.next=now+1.0/15;trail.points.Enqueue(normalized);while(trail.points.Count>120)trail.points.Dequeue();}
                        if(Event.current.type==EventType.Repaint){Handles.BeginGUI();Handles.color=new Color(.2f,.7f,.8f,.4f);bool first=true;Vector3 previous=default;foreach(var sample in trail.points){var q=Project(sample,view);Vector3 current=new Vector3(area.x+(q.x+1)*area.width*.5f,area.y+(1-q.y)*area.height*.5f);if(!first)Handles.DrawLine(previous,current);first=false;previous=current;}Handles.EndGUI();}
                    }
                }
                Rect hit=new Rect(point.x-8,point.y-8,16,16);
                if(Event.current.type==EventType.Repaint)
                {
                    Handles.BeginGUI();Handles.color=Selection.activeGameObject==obj.gameObject?Color.yellow:obj.mute?Color.gray:new Color(.2f,.85f,1);
                    Handles.DrawSolidDisc(point,Vector3.forward,5);Handles.EndGUI();
                }
                string text=$"{obj.DisplayName} | ADM {obj.AdmId}, Custom {obj.CustomId}\nStage front/right/up: {normalized}\nMeters: {manager.stage.Meters(normalized)}";
                if(GUI.Button(hit,new GUIContent("",text),GUIStyle.none)){Selection.activeGameObject=obj.gameObject;EditorGUIUtility.PingObject(obj.gameObject);}
                GUI.Label(new Rect(Mathf.Clamp(point.x+8,area.x,Mathf.Max(area.x,area.xMax-120)),Mathf.Clamp(point.y-9,area.y,area.yMax-18),120,18),obj.objectId+"  "+obj.name,EditorStyles.miniLabel);
            }
            EditorGUILayout.HelpBox("Normalized stage front/right/up snapshot. Click a point to select its object. This is not network acknowledgement or physical sound tracking.",MessageType.None);
        }
    }
}
