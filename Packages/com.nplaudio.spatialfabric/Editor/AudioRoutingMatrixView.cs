// Rectangular, offline-first crosspoint authoring. No device discovery or playback. MIT.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NPLAudio.SpatialFabric.Editor
{
    public sealed class AudioRoutingMatrixView
    {
        public const float CellWidth=28, RowHeight=24, RowHeaderWidth=210, GroupHeaderHeight=100, ChannelHeaderHeight=30;
        const float ScrollbarSize=16;
        const float HeaderHeight=GroupHeaderHeight+ChannelHeaderHeight;

        public sealed class Row
        {
            public PlaybackCue Cue {get;internal set;}
            public string Name {get;internal set;}
            public string Tooltip {get;internal set;}
            public string DeviceId {get;internal set;}
            public int Channel {get;internal set;}
            public bool Editable {get;internal set;}
            public string Reason {get;internal set;}
        }
        // A compact span, not one object per channel (up to 4096 channels per cached interface).
        public sealed class Group
        {
            public string DeviceId {get;internal set;}
            public string Title {get;internal set;}
            public string Tooltip {get;internal set;}
            public int Capacity {get;internal set;}
            public bool Known {get;internal set;}
            public bool Online {get;internal set;}
            public int Start {get;internal set;}
            public int[] StoredChannels {get;internal set;}
            public int Count=>1+Capacity+StoredChannels.Length;
            public int ChannelAt(int offset)
            {
                if(offset<0 || offset>=Count)throw new ArgumentOutOfRangeException(nameof(offset));
                return offset<=Capacity?offset:StoredChannels[offset-Capacity-1];
            }
        }
        public readonly struct Column
        {
            public readonly Group Group;
            public readonly int Channel;
            public bool Stored=>Channel>Group.Capacity;
            public string Label=>Channel==0?"Auto":Channel+(Stored?"*":"");
            public string Tooltip=>Group.Tooltip+"\n"+(Channel==0?"Auto 0: corresponding source/output channels":
                "Output "+Channel+": receives the first source channel"+(Stored?" · stored route ("+(Group.Known?"outside cached capacity":"capacity unknown")+")":""));
            public Column(Group group,int channel){Group=group;Channel=channel;}
        }
        public sealed class Presentation
        {
            public Row[] Rows {get;internal set;}=Array.Empty<Row>();
            public Group[] Groups {get;internal set;}=Array.Empty<Group>();
            public int ColumnCount {get;internal set;}
            public bool EmptyCache {get;internal set;}
            public Column ColumnAt(int index)
            {
                if(index<0 || index>=ColumnCount)throw new ArgumentOutOfRangeException(nameof(index));
                int lo=0,hi=Groups.Length-1;
                while(lo<=hi)
                {
                    int mid=(lo+hi)/2;var g=Groups[mid];
                    if(index<g.Start)hi=mid-1;
                    else if(index>=g.Start+g.Count)lo=mid+1;
                    else return new Column(g,g.ChannelAt(index-g.Start));
                }
                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
        public readonly struct VisibleRange
        {
            public readonly int First,End;
            public int Count=>End-First;
            public VisibleRange(int first,int end){First=first;End=end;}
        }
        public static VisibleRange Visible(float offset,float extent,float stride,int count)
        {
            if(stride<=0 || extent<=0 || count<=0)return new VisibleRange(0,0);
            int first=Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(0,offset)/stride),0,count);
            int end=Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(0,offset)+extent)/stride),first,count);
            return new VisibleRange(first,end);
        }
        public static Rect CellRect(int row,int column,Vector2 offset)=>new Rect(column*CellWidth-offset.x,row*RowHeight-offset.y,CellWidth,RowHeight);
        public static Rect Intersection(Rect a,Rect b)
        {
            float x=Mathf.Max(a.xMin,b.xMin),y=Mathf.Max(a.yMin,b.yMin),right=Mathf.Min(a.xMax,b.xMax),bottom=Mathf.Min(a.yMax,b.yMax);
            return right>x && bottom>y?Rect.MinMaxRect(x,y,right,bottom):new Rect(x,y,0,0);
        }
        public static Vector2 WheelScroll(Vector2 current,Vector2 delta,bool shift,Vector2 viewport,Vector2 content)
        {
            var next=current+(shift?new Vector2(delta.y*CellWidth*3,0):new Vector2(delta.x*CellWidth*3,delta.y*RowHeight*3));
            return new Vector2(Mathf.Clamp(next.x,0,Mathf.Max(0,content.x-viewport.x)),Mathf.Clamp(next.y,0,Mathf.Max(0,content.y-viewport.y)));
        }

        // Called ONLY on Layout by Draw. The returned data contains no live presentation getters.
        // Also exposed for hardware-free mapping/geometry tests.
        public static Presentation Capture(PlaybackCue[] cues,AudioInterfaceCache.Snapshot[] devices)
        {
            var result=new Presentation();
            result.Rows=(cues??Array.Empty<PlaybackCue>()).Where(c=>c!=null).Distinct().Select(c=>
            {
                bool editable=AudioRoutingPanel.CanAssign(c,out string reason);
                var names=new List<string>();
                for(var t=c.transform;t!=null;t=t.parent)names.Add(t.name);
                names.Reverse();
                return new Row{Cue=c,Name=c.name,Tooltip=(string.IsNullOrEmpty(c.gameObject.scene.path)?c.gameObject.scene.name:c.gameObject.scene.path)+"\n"+string.Join("/",names),
                    DeviceId=c.configuredDeviceId??"",Channel=c.outputChannel,Editable=editable,Reason=reason};
            }).ToArray();
            var snapshots=(devices??Array.Empty<AudioInterfaceCache.Snapshot>()).Where(d=>d!=null && !string.IsNullOrEmpty(d.id)).ToArray();
            result.EmptyCache=snapshots.Length==0;
            var groups=new List<Group>();var ids=new HashSet<string>(StringComparer.Ordinal);
            // Index authored channels once; many unknown per-cue IDs must not rescan all rows per group.
            var authored=result.Rows.GroupBy(r=>r.DeviceId,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Select(r=>r.Channel).Distinct().ToArray(),StringComparer.Ordinal);
            Action<string,AudioInterfaceCache.Snapshot> add=(id,d)=>
            {
                if(!ids.Add(id))return;
                bool known=d!=null;int capacity=known?Mathf.Clamp(d.channels,0,AudioInterfaceCache.MaxChannels):0;
                var stored=authored.TryGetValue(id,out var channels)?channels.Where(c=>c>capacity).OrderBy(c=>c).ToArray():Array.Empty<int>();
                string title=id.Length==0?"Dynamic OS default":known?(string.IsNullOrEmpty(d.name)?id:d.name)+" ["+id+"]"+(d.online?"":" · OFFLINE / cached"):id+" · UNKNOWN";
                string tooltip=id.Length==0?"Dynamic OS default (empty ID). Follows the OS at playback time; not a fixed hardware endpoint. Capacity unknown.":
                    known?title+"\nExact ID: "+id+"\n"+capacity+" cached output channels · "+d.sampleRate+" Hz · last seen "+d.lastSeenUtc:
                    "Exact ID: "+id+" · UNKNOWN / offline. Capacity unknown; only Auto 0 and explicitly stored channels are shown.";
                groups.Add(new Group{DeviceId=id,Title=title,Tooltip=tooltip,Known=known,Online=known && d.online,Capacity=capacity,StoredChannels=stored,Start=result.ColumnCount});
                result.ColumnCount+=groups[groups.Count-1].Count;
            };
            add("",null);
            foreach(var d in snapshots)add(d.id,d);
            foreach(var r in result.Rows)add(r.DeviceId,null);
            result.Groups=groups.ToArray();return result;
        }

        Presentation presentation=new Presentation();
        Vector2 scroll,layoutScroll;
        Vector2 lastBodySize=new Vector2(560,230);
        string message="",layoutMessage="";
        GUIStyle small,center,heading,check;
        (PlaybackCue cue,string device,int channel)? pressed;
        // Visible, clipped cell rectangles in SCREEN coordinates from the latest Repaint.
        // Kept under this exact name for native MouseDown/MouseUp integration tests.
        readonly Dictionary<(PlaybackCue cue,string device,int channel),Rect> paintedCellScreens=new Dictionary<(PlaybackCue cue,string device,int channel),Rect>();
        public AudioRoutingMatrixView(){} // In particular: no cache construction, probe or native calls.

        public void Draw(PlaybackCue[] cues,AudioInterfaceCache.Snapshot[] devices,Rect visibleScreen)
        {
            if(Event.current.type==EventType.Layout)
            {
                presentation=Capture(cues,devices);layoutMessage=message;
                scroll.x=Mathf.Clamp(scroll.x,0,Mathf.Max(0,presentation.ColumnCount*CellWidth-lastBodySize.x));
                scroll.y=Mathf.Clamp(scroll.y,0,Mathf.Max(0,presentation.Rows.Length*RowHeight-lastBodySize.y));
                layoutScroll=scroll;
            }
            EnsureStyles();
            EditorGUILayout.LabelField("Crosspoint routing · one endpoint/channel per cue · checks = configured, not live",EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Auto 0 = corresponding source channels. 1+ = first source → output. * = stored, capacity unverified.",EditorStyles.miniLabel);
            if(presentation.EmptyCache)EditorGUILayout.HelpBox("No cached interfaces. Use Refresh Interfaces to explicitly discover outputs. Dynamic default and stored unknown routes remain available below.",MessageType.Info);
            if(presentation.Rows.Length==0)EditorGUILayout.HelpBox("No playback cues. Output columns remain available; add a PlaybackCue to route it.",MessageType.Info);
            if(!string.IsNullOrEmpty(layoutMessage))EditorGUILayout.HelpBox(layoutMessage,MessageType.Warning);
            Rect bounds=GUILayoutUtility.GetRect(330,10000,300,480,GUILayout.ExpandWidth(true),GUILayout.ExpandHeight(true));
            DrawMatrix(bounds,visibleScreen);
        }
        void EnsureStyles()
        {
            if(small!=null)return;
            small=new GUIStyle(EditorStyles.miniLabel){clipping=TextClipping.Clip};
            center=new GUIStyle(small){alignment=TextAnchor.MiddleCenter};
            heading=new GUIStyle(small){alignment=TextAnchor.MiddleCenter,wordWrap=true,fontStyle=FontStyle.Bold};
            check=new GUIStyle(center){fontSize=18,fontStyle=FontStyle.Bold};
            check.normal.textColor=Color.white;
        }
        void DrawMatrix(Rect bounds,Rect visibleScreen)
        {
            // Exactly one control ID, independent of cue/channel count or destroyed live objects.
            int id=GUIUtility.GetControlID("SpatialFabric.CrosspointMatrix".GetHashCode(),FocusType.Passive,bounds);
            float rowWidth=Mathf.Min(RowHeaderWidth,Mathf.Max(80,bounds.width-ScrollbarSize-CellWidth));
            Rect body=new Rect(bounds.x+rowWidth,bounds.y+HeaderHeight,Mathf.Max(1,bounds.width-rowWidth-ScrollbarSize),Mathf.Max(1,bounds.height-HeaderHeight-ScrollbarSize));
            if(Event.current.type==EventType.Repaint)lastBodySize=body.size;
            float width=presentation.ColumnCount*CellWidth,height=presentation.Rows.Length*RowHeight;
            if(Event.current.type==EventType.Repaint)
            {
                paintedCellScreens.Clear();
                EditorGUI.DrawRect(bounds,new Color(.14f,.15f,.16f));
            }
            scroll.x=GUI.HorizontalScrollbar(new Rect(body.x,body.yMax,body.width,ScrollbarSize),scroll.x,body.width,0,Mathf.Max(body.width,width));
            scroll.y=GUI.VerticalScrollbar(new Rect(body.xMax,body.y,ScrollbarSize,body.height),scroll.y,body.height,0,Mathf.Max(body.height,height));
            var e=Event.current;
            bool inViewport=visibleScreen.Contains(GUIUtility.GUIToScreenPoint(e.mousePosition));
            if(e.type==EventType.ScrollWheel && bounds.Contains(e.mousePosition) && inViewport)
            {
                var next=WheelScroll(scroll,e.delta,e.shift,body.size,new Vector2(width,height));
                if(next!=scroll){scroll=next;GUI.changed=true;e.Use();}
            }
            var columns=Visible(layoutScroll.x,body.width,CellWidth,presentation.ColumnCount);
            var rows=Visible(layoutScroll.y,body.height,RowHeight,presentation.Rows.Length);
            bool over=inViewport && body.Contains(e.mousePosition);
            int hoverColumn=over?Mathf.FloorToInt((e.mousePosition.x-body.x+layoutScroll.x)/CellWidth):-1;
            int hoverRow=over?Mathf.FloorToInt((e.mousePosition.y-body.y+layoutScroll.y)/RowHeight):-1;
            bool hit=hoverColumn>=0 && hoverColumn<presentation.ColumnCount && hoverRow>=0 && hoverRow<presentation.Rows.Length;
            HandleClick(id,hit,hoverRow,hoverColumn);
            // Headers are separate clipped regions: they never move on the other scroll axis.
            GUI.BeginGroup(new Rect(bounds.x,body.y,rowWidth,body.height));
            for(int r=rows.First;r<rows.End;r++)
            {
                var row=presentation.Rows[r];Rect rect=new Rect(0,r*RowHeight-layoutScroll.y,rowWidth,RowHeight);
                if(e.type==EventType.Repaint)EditorGUI.DrawRect(rect,r==hoverRow?new Color(.20f,.39f,.40f):new Color(.23f,.24f,.25f));
                GUI.Label(new Rect(rect.x+5,rect.y,rect.width-8,rect.height),new GUIContent(row.Name,row.Tooltip+(row.Editable?"":"\n"+row.Reason)),small);
            }
            GUI.EndGroup();
            GUI.BeginGroup(new Rect(body.x,bounds.y,body.width,HeaderHeight));
            foreach(var group in presentation.Groups)
            {
                float x=group.Start*CellWidth-layoutScroll.x,w=group.Count*CellWidth;
                if(x+w<=0 || x>=body.width)continue;
                Rect header=new Rect(x,0,w-1,GroupHeaderHeight-1);
                if(e.type==EventType.Repaint)EditorGUI.DrawRect(header,group.DeviceId.Length==0?new Color(.25f,.30f,.33f):group.Online?new Color(.26f,.29f,.31f):new Color(.30f,.28f,.25f));
                // Narrow groups get Dante-style vertical labels, not unreadable one-character strips.
                if(w<140)
                {
                    Matrix4x4 matrix=GUI.matrix;
                    GUIUtility.RotateAroundPivot(-90,new Vector2(x+w/2,GroupHeaderHeight/2));
                    GUI.Label(new Rect(x+w/2-GroupHeaderHeight/2,GroupHeaderHeight/2-w/2,GroupHeaderHeight,w),new GUIContent(group.Title,group.Tooltip),center);
                    GUI.matrix=matrix;
                }
                else
                {
                    // Keep a wide interface's title in view even when most of its channels are offscreen.
                    Rect visibleHeader=Rect.MinMaxRect(Mathf.Max(0,header.x),header.y,Mathf.Min(body.width,header.xMax),header.yMax);
                    GUI.Label(visibleHeader,new GUIContent(group.Title,group.Tooltip),heading);
                }
            }
            for(int c=columns.First;c<columns.End;c++)
            {
                var column=presentation.ColumnAt(c);Rect rect=new Rect(c*CellWidth-layoutScroll.x,GroupHeaderHeight,CellWidth-1,ChannelHeaderHeight-1);
                if(e.type==EventType.Repaint)EditorGUI.DrawRect(rect,c==hoverColumn?new Color(.20f,.39f,.40f):new Color(.30f,.31f,.32f));
                GUI.Label(rect,new GUIContent(column.Label,column.Tooltip),center);
            }
            GUI.EndGroup();
            GUI.Label(new Rect(bounds.x+5,bounds.y+GroupHeaderHeight,rowWidth-10,ChannelHeaderHeight),new GUIContent("Playback cue ↓ / Output →","Each row selects one endpoint/channel, not a per-source DSP or multi-send matrix."),small);
            GUI.BeginGroup(body);
            for(int r=rows.First;r<rows.End;r++)
            {
                var row=presentation.Rows[r];
                for(int c=columns.First;c<columns.End;c++)
                {
                    var column=presentation.ColumnAt(c);Rect cell=CellRect(r,c,layoutScroll);
                    bool selected=string.Equals(row.DeviceId,column.Group.DeviceId,StringComparison.Ordinal) && row.Channel==column.Channel;
                    if(e.type==EventType.Repaint)
                    {
                        var fill=selected?new Color(.13f,.57f,.24f):r==hoverRow || c==hoverColumn?new Color(.24f,.39f,.39f):r%2==0?new Color(.31f,.32f,.33f):new Color(.27f,.28f,.29f);
                        if(!row.Editable)fill*=.7f;
                        EditorGUI.DrawRect(new Rect(cell.x+1,cell.y+1,cell.width-1,cell.height-1),fill);
                        Rect clipped=Rect.MinMaxRect(Mathf.Max(0,cell.x),Mathf.Max(0,cell.y),Mathf.Min(body.width,cell.xMax),Mathf.Min(body.height,cell.yMax));
                        var screen=Intersection(GUIUtility.GUIToScreenRect(clipped),visibleScreen);
                        if(screen.width>0 && screen.height>0)paintedCellScreens[(row.Cue,column.Group.DeviceId,column.Channel)]=screen;
                    }
                    GUI.Label(cell,new GUIContent(selected?"✓":"",row.Name+"\n"+column.Tooltip+(row.Editable?"":"\n"+row.Reason)),check);
                }
            }
            GUI.EndGroup();
        }
        void HandleClick(int id,bool hit,int rowIndex,int columnIndex)
        {
            var e=Event.current;
            if(e.type==EventType.MouseDown && e.button==0 && hit && GUI.enabled)
            {
                var row=presentation.Rows[rowIndex];
                if(!row.Editable)return;
                var column=presentation.ColumnAt(columnIndex);
                pressed=(row.Cue,column.Group.DeviceId,column.Channel);GUIUtility.hotControl=id;e.Use();
            }
            else if(e.type==EventType.MouseUp && e.button==0 && GUIUtility.hotControl==id)
            {
                GUIUtility.hotControl=0;var target=pressed;pressed=null;e.Use();
                if(!hit || !target.HasValue || !GUI.enabled)return;
                var row=presentation.Rows[rowIndex];var column=presentation.ColumnAt(columnIndex);var p=target.Value;
                if(!ReferenceEquals(row.Cue,p.cue) || !string.Equals(column.Group.DeviceId,p.device,StringComparison.Ordinal) || column.Channel!=p.channel)return;
                // Fresh guarded Undo mutation, even if worker/route/object state changed since Layout.
                if(AudioRoutingPanel.AssignRoute(p.cue,p.device,p.channel,out message))GUI.changed=true;
            }
            else if(e.type==EventType.Ignore && GUIUtility.hotControl==id){GUIUtility.hotControl=0;pressed=null;}
        }
    }
}
