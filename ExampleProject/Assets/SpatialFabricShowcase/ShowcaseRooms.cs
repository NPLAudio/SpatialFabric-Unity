// Original four-room example. Geometry uses canonical front/right/up centimeters. MIT.
using System;
using UnityEngine;
namespace NPLAudio.SpatialFabric.Example
{
    [Serializable] public sealed class RoomDefinition { public string name; public float[] bounds; public float height,doorWidth,centerRight; }
    [Serializable] public sealed class RoomBox { public string name,material;public float[] position,size;public bool collision; }
    [Serializable] public sealed class RoomFloorSample { public string name;public float[] point; }
    [Serializable] public sealed class RoomSegment { public string name;public float[] start,end; }
    [Serializable] public sealed class RoomPlan
    {
        public string format;public RoomDefinition[] rooms;public RoomBox[] boxes;
        public float[] spawn;public float yaw;public RoomFloorSample[] floorSamples;public RoomSegment[] doors,walls;
    }
    public sealed class ShowcaseRooms : MonoBehaviour
    {
        public SpatialFabricManager manager;
        public ShowcaseVisitor visitor;
        public PlaybackCue[] cues;
        public SpatialObject[] trackedObjects;
        public RoomPlan plan;
        public int visualVersion;
        public const string RoomScenePath="Assets/SpatialFabricShowcase/Scenes/RoomShowcase.unity";
        public static Vector3 Point(float[] p)=>new Vector3(p[1]*.01f,p[2]*.01f,p[0]*.01f);
        public static Vector3 Size(float[] p)=>new Vector3(p[1]*.01f,p[2]*.01f,p[0]*.01f);
        public int RoomAt(Vector3 position)
        {
            if(plan?.rooms==null)return -1;
            for(int i=0;i<plan.rooms.Length;++i){var b=plan.rooms[i].bounds;if(position.z>=b[0]*.01f && position.z<=b[1]*.01f && position.x>=b[2]*.01f && position.x<=b[3]*.01f)return i;}
            return -1;
        }
        public PlaybackCue CurrentCue { get {int room=visitor==null?-1:RoomAt(visitor.transform.position);return room>=1 && room<=3 && cues!=null && cues.Length==3?cues[room-1]:null;} }
        public void StopAll(){if(cues!=null)foreach(var cue in cues)if(cue!=null)cue.Stop();}
    }
}
