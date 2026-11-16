// Silent geometry/default validation shared by editor and real player. No transport/hardware calls. MIT.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace NPLAudio.SpatialFabric.Example
{
    [Serializable] public sealed class RoomValidationReport
    {
        public string status;
        public List<string> passed=new List<string>(),failed=new List<string>();
        public void Check(bool value,string name){(value?passed:failed).Add(name);status=failed.Count==0?"passed":"failed";}
    }
    public static class RoomValidation
    {
        public static RoomValidationReport Validate(ShowcaseRooms rooms,bool actualVisitorMoves)
        {
            var r=new RoomValidationReport();Physics.SyncTransforms();
            r.Check(rooms!=null && rooms.plan.rooms.Length==4,"four room definitions");if(rooms==null)return r;
            var pipeline=UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            r.Check(pipeline!=null && pipeline.GetType().Name=="UniversalRenderPipelineAsset","URP default pipeline assigned");
            r.Check(QualitySettings.renderPipeline==pipeline,"active quality uses showcase URP");
            var materials=rooms.GetComponentsInChildren<Renderer>(true).SelectMany(m=>m.sharedMaterials).Where(m=>m!=null).ToArray();
            r.Check(materials.Length>0 && materials.All(m=>m.shader!=null && (m.shader.name=="Universal Render Pipeline/Lit" || m.shader.name=="SpatialFabric/Room Text")),"URP geometry and depth-tested sign materials");
            r.Check(rooms.cues.Length==3 && rooms.cues.All(c=>c.preparedAudio!=null),"three real prepared PCM assets");
            r.Check(rooms.cues.Select(c=>c.preparedAudio.Channels).SequenceEqual(new[]{1,2,1}),"mono stereo replay channels");
            r.Check(rooms.cues.All(c=>c.preparedAudio.SampleRate==48000 && c.preparedAudio.Duration>.1),"48 kHz original cue durations");
            r.Check(rooms.cues.All(c=>!c.IsPlaying && !c.IsPrepared && !c.ReplayPending),"no autoplay or automatic PCM preparation");
            r.Check(!rooms.manager.IsListening && !rooms.manager.enableInPlay && !rooms.manager.enableInEditor,"silent OSC startup");
            r.Check(!rooms.manager.stage.followPositionAndOrientation,"fixed stage basis");
            r.Check(rooms.trackedObjects.Length==4 && rooms.trackedObjects.Count(o=>o.GetComponent<RoomMotion>()!=null)==3,"four objects three animated");
            r.Check(rooms.trackedObjects.All(o=>o.GetComponent<Collider>()==null),"tracked visuals do not collide");
            r.Check(rooms.cues[0].outputChannel==1 && rooms.cues[1].outputChannel==0 && rooms.cues[2].outputChannel==2,"output routing 1 matching 2");
            r.Check(rooms.cues[1].loop && rooms.cues[2].autoReplay && !rooms.cues[2].loop && Math.Abs(rooms.cues[2].replayDelaySeconds-2.5f)<.001f,"stereo loop and replay defaults");
            foreach(var sample in rooms.plan.floorSamples)
            {
                Vector3 p=new Vector3(sample.point[1]*.01f,.5f,sample.point[0]*.01f);
                r.Check(Physics.Raycast(p,Vector3.down,out var hit,2) && hit.collider.name=="Gallery foundation","floor / "+sample.name);
            }
            foreach(var segment in rooms.plan.doors)
            {
                Vector3 a=ShowcaseRooms.Point(segment.start),b=ShowcaseRooms.Point(segment.end),delta=b-a;
                bool clear=!Physics.CheckCapsule(a+Vector3.down*.6f,a+Vector3.up*.6f,.3f) && !Physics.CapsuleCast(a+Vector3.down*.6f,a+Vector3.up*.6f,.3f,delta.normalized,delta.magnitude);
                r.Check(clear,"capsule door / "+segment.name);
            }
            foreach(var segment in rooms.plan.walls)
            {
                Vector3 a=ShowcaseRooms.Point(segment.start),b=ShowcaseRooms.Point(segment.end),delta=b-a;
                r.Check(Physics.CapsuleCast(a+Vector3.down*.6f,a+Vector3.up*.6f,.3f,delta.normalized,delta.magnitude),"wall blocking / "+segment.name);
            }
            if(actualVisitorMoves)
            {
                var visitor=rooms.visitor;Vector3 original=visitor.transform.position;bool automation=visitor.automationMode;visitor.automationMode=true;
                foreach(var segment in rooms.plan.doors)
                {
                    Vector3 a=ShowcaseRooms.Point(segment.start);a.y=.08f;Vector3 b=ShowcaseRooms.Point(segment.end);b.y=.08f;visitor.Teleport(a);
                    Vector3 step=(b-a)/30;for(int i=0;i<30;++i)visitor.Body.Move(step+Vector3.down*.03f);
                    Vector3 error=visitor.transform.position-b;error.y=0;r.Check(error.magnitude<.15f,"visitor passage / "+segment.name);
                }
                foreach(var segment in rooms.plan.walls)
                {
                    Vector3 a=ShowcaseRooms.Point(segment.start);a.y=.08f;Vector3 b=ShowcaseRooms.Point(segment.end);b.y=.08f;visitor.Teleport(a);
                    Vector3 step=(b-a)/30;for(int i=0;i<30;++i)visitor.Body.Move(step+Vector3.down*.03f);
                    Vector3 error=visitor.transform.position-b;error.y=0;r.Check(error.magnitude>.25f,"visitor blocked / "+segment.name);
                }
                visitor.Teleport(original);for(int i=0;i<15;++i)visitor.Body.Move(Vector3.down*.03f);
                r.Check(visitor.Body.isGrounded && visitor.transform.position.y>-.05f && visitor.transform.position.y<.15f,"visitor hallway landing");visitor.automationMode=automation;
            }
            return r;
        }
    }
}
