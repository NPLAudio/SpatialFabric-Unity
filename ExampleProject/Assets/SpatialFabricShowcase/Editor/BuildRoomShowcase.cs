// Original minimal room generator. No template assets, imported meshes or autoplay. MIT.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using NPLAudio.SpatialFabric.Audio;
namespace NPLAudio.SpatialFabric.Example.Editor
{
    public static class BuildRoomShowcase
    {
        const string Generated="Assets/SpatialFabricShowcase/GeneratedRooms";
        static Transform root;
        [MenuItem("Tools/SpatialFabric/Build four-room showcase (new scene)")]
        public static void Interactive()
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            if(File.Exists(ShowcaseRooms.RoomScenePath)){EditorUtility.DisplayDialog("Room scene already exists","The generator will not overwrite it. Open the existing scene or back it up and remove it explicitly.","OK");return;}
            BuildBatch();
        }
        static Material Material(string name,Color color)
        {
            string path=Generated+"/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
            material.color=color;material.SetFloat("_Smoothness",.05f);EditorUtility.SetDirty(material);return material;
        }
        static GameObject Box(string name,Vector3 position,Vector3 size,Material material,bool collision)
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;obj.transform.SetParent(root);obj.transform.position=position;obj.transform.localScale=size;obj.GetComponent<Renderer>().sharedMaterial=material;
            if(!collision)UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());return obj;
        }
        static void Sign(string name,string text,float front,float right,float up,float size,float yaw=0,bool floor=false)
        {
            var obj=new GameObject(name);obj.transform.SetParent(root);obj.transform.position=new Vector3(right,up,front);
            obj.transform.rotation=floor?Quaternion.Euler(90,0,0):Quaternion.Euler(0,yaw,0);
            var label=obj.AddComponent<TextMesh>();label.text=text;label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.fontSize=96;label.characterSize=size*.1f;label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;label.color=new Color(.9f,.95f,1);
            obj.GetComponent<MeshRenderer>().sharedMaterial=TextMaterial(label.font);
            obj.AddComponent<RoomSign>();
        }
        public static Material TextMaterial(Font font)
        {
            string path=Generated+"/RoomText.mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(Shader.Find("SpatialFabric/Room Text"));AssetDatabase.CreateAsset(m,path);}
            m.mainTexture=font.material.mainTexture;EditorUtility.SetDirty(m);return m;
        }
        static void Light(string name,Vector3 position,float intensity,float range)
        {
            var l=new GameObject(name).AddComponent<UnityEngine.Light>();l.transform.SetParent(root);l.transform.position=position;l.type=LightType.Point;l.intensity=intensity*(name.StartsWith("Spatial fill ")?12f:4f);l.range=range;l.shadows=LightShadows.None;
        }
        static PreparedAudioAsset Audio(string file)
        {
            string path=Generated+"/"+Path.GetFileNameWithoutExtension(file)+".asset";
            if(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path)!=null)throw new InvalidOperationException("Refusing to overwrite generated PCM asset "+path);
            var pcm=WavDecoder.Decode(File.ReadAllBytes(Path.Combine(Application.dataPath,"../SourceAssets/Audio",file)));
            var asset=ScriptableObject.CreateInstance<PreparedAudioAsset>();asset.SetPcm(pcm,file);AssetDatabase.CreateAsset(asset,path);return asset;
        }
        public static void BuildBatch()
        {
            if(File.Exists(ShowcaseRooms.RoomScenePath))throw new InvalidOperationException("Room scene already exists; no overwrite");
            string marker=Generated+"/.spatialfabric-owned";
            if(Directory.Exists(Generated))throw new InvalidOperationException("GeneratedRooms exists; back up/review it before regenerating");
            var plan=JsonUtility.FromJson<RoomPlan>(File.ReadAllText(Path.Combine(Application.dataPath,"../SourceAssets/RoomLayout.json")));
            if(plan.format!="SpatialFabric.Unity.RoomPlan/1" || plan.rooms.Length!=4)throw new InvalidOperationException("Unknown room layout");
            Directory.CreateDirectory(Generated);File.WriteAllText(marker,"SpatialFabric original room assets v1\n");AssetDatabase.Refresh();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var scene=new GameObject("SpatialFabric Four Rooms").AddComponent<ShowcaseRooms>();root=scene.transform;scene.plan=plan;scene.visualVersion=1;
            var wall=Material("RoomWall",new Color(.28f,.32f,.37f));var floor=Material("Floor",new Color(.15f,.19f,.23f));var ceiling=Material("Ceiling",new Color(.35f,.39f,.43f));var grid=Material("StageOutline",new Color(.25f,.64f,.78f));
            var accents=new[]{Material("Motion",new Color(.15f,.75f,.95f)),Material("Mono",new Color(.95f,.5f,.2f)),Material("Stereo",new Color(.25f,.85f,.55f)),Material("Replay",new Color(.75f,.45f,.95f))};
            foreach(var b in plan.boxes)
            {
                Material m=b.material=="floor"?floor:b.material=="ceiling"?ceiling:b.material=="grid"?grid:b.material.StartsWith("accent")?accents[int.Parse(b.material.Substring(6))]:wall;
                Box(b.name,ShowcaseRooms.Point(b.position),ShowcaseRooms.Size(b.size),m,b.collision);
            }
            var stage=new GameObject("Stage — fixed canonical basis").AddComponent<SpatialStage>();stage.transform.SetParent(root);stage.transform.position=new Vector3(0,4.5f,0);stage.dimensionsMeters=new Vector3(20,15,8);stage.followPositionAndOrientation=false;
            var manager=new GameObject("SpatialFabric Manager").AddComponent<SpatialFabricManager>();manager.transform.SetParent(root);manager.stage=stage;
            manager.spatialInputPort=8100;manager.playbackInputPort=9000;manager.admPort=50018;manager.customPort=9002;manager.monitorPort=9001;manager.enableInPlay=manager.enableInEditor=false;scene.manager=manager;
            scene.trackedObjects=new SpatialObject[4];
            Vector3[] positions={new Vector3(0,2,5),new Vector3(3,3,-3),new Vector3(-4,4,1),new Vector3(0,2,0)};
            for(int i=0;i<4;++i)
            {
                var obj=GameObject.CreatePrimitive(PrimitiveType.Sphere);obj.name="OSC object "+(i+1);obj.transform.SetParent(root);obj.transform.position=positions[i];obj.transform.localScale=Vector3.one*.55f;UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());obj.GetComponent<Renderer>().sharedMaterial=accents[i];
                var binding=obj.AddComponent<SpatialObject>();binding.objectId=i+1;manager.objects.Add(binding);scene.trackedObjects[i]=binding;
                if(i<3){var motion=obj.AddComponent<RoomMotion>();motion.origin=positions[i];motion.amplitude=new Vector3(1.5f,.6f,2);motion.frequency=.35f+i*.17f;motion.phase=i*.7f;}
            }
            scene.cues=new PlaybackCue[3];string[] files={"SW_SF_MonoCue.wav","SW_SF_StereoLoop.wav","SW_SF_ReplayCue.wav"};string[] ids={"mono","stereo","replay"};
            for(int i=0;i<3;++i)
            {
                var cue=new GameObject("Cue — "+plan.rooms[i+1].name).AddComponent<PlaybackCue>();cue.transform.SetParent(root);cue.transform.position=new Vector3(plan.rooms[i+1].centerRight*.01f,1.1f,-6.5f);cue.preparedAudio=Audio(files[i]);cue.oscBase="/unity/playback/"+ids[i];cue.outputChannel=i==0?1:i==1?0:2;cue.loop=i==1;cue.autoReplay=i==2;cue.replayDelaySeconds=2.5f;scene.cues[i]=cue;
            }
            var visitor=new GameObject("Visitor — WASD + mouse").AddComponent<ShowcaseVisitor>();visitor.transform.SetParent(root);visitor.transform.position=new Vector3(plan.spawn[1]*.01f,.08f,plan.spawn[0]*.01f);visitor.transform.rotation=Quaternion.Euler(0,plan.yaw,0);
            var body=visitor.GetComponent<CharacterController>();body.height=1.8f;body.radius=.3f;body.center=new Vector3(0,.9f,0);body.skinWidth=.04f;body.stepOffset=.25f;
            var camera=new GameObject("Visitor camera").AddComponent<Camera>();camera.transform.SetParent(visitor.transform,false);camera.transform.localPosition=new Vector3(0,1.65f,0);camera.tag="MainCamera";camera.fieldOfView=70;camera.nearClipPlane=.05f;camera.farClipPlane=150;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.1f,.13f);
            visitor.viewCamera=camera;visitor.rooms=scene;scene.visitor=visitor;var hud=scene.gameObject.AddComponent<ShowcaseRoomHud>();hud.rooms=scene;
            Sign("Hall guide","SPATIALFABRIC\nWASD + mouse | Esc cursor\nE play / Q stop / X stop all",-17.47f,-4.5f,2.3f,.24f,180);
            for(int i=0;i<4;++i)Sign("Room name "+i,(i+1)+" / "+plan.rooms[i].name.ToUpperInvariant(),-12.13f,plan.rooms[i].centerRight*.01f,3.25f,.3f);
            Sign("Spatial instructions","4 OSC OBJECTS / 3 ANIMATED\nStart OSC explicitly in HUD\nADM 50018 / Custom 9002",11.97f,0,2.6f,.28f);
            Sign("Mono instructions","WARM PERCUSSION / OUTPUT 1\nE play / Q stop\n/unity/playback/mono",-2.13f,13.5f,2.35f,.23f);
            Sign("Stereo instructions","STEREO GROOVE / MATCHING L-R\nE play / Q stop | L loop\n/unity/playback/stereo",-2.13f,21.5f,2.35f,.23f);
            Sign("Replay instructions","GLASS REPLAY / OUTPUT 2\nE start / Q cancel | R auto replay\n/unity/playback/replay",-2.13f,29.5f,2.35f,.23f);
            Sign("Front axis","+FRONT / UNITY +Z",9,0,.03f,.25f,0,true);Sign("Right axis","+RIGHT / UNITY +X",0,6.5f,.03f,.25f,0,true);
            Light("Spatial fill A",new Vector3(-3,7.8f,-4.5f),3,24);Light("Spatial fill B",new Vector3(3,7.8f,4.5f),3,24);
            for(int i=1;i<4;++i)Light("Room fill "+i,new Vector3(plan.rooms[i].centerRight*.01f,3.7f,-7),2.5f,14);
            foreach(float right in new[]{-5f,10f,25f})Light("Hall fill "+right,new Vector3(right,3.15f,-14.8f),2,12);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.24f,.27f,.32f);RenderSettings.fog=false;QualitySettings.pixelLightCount=8;
            Directory.CreateDirectory(Path.GetDirectoryName(ShowcaseRooms.RoomScenePath));AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene.gameObject.scene,ShowcaseRooms.RoomScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ShowcaseRooms.RoomScenePath,true)};Selection.activeGameObject=manager.gameObject;
            Debug.Log("Four-room showcase saved. Audio and OSC stopped. Walking/real cue controls are ready in Play mode.");
        }
        [MenuItem("Tools/SpatialFabric/Open four-room showcase")]
        public static void Open()
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.OpenScene(ShowcaseRooms.RoomScenePath);
            var rooms=UnityObjects.FindAll<ShowcaseRooms>()[0];Selection.activeGameObject=rooms.manager.gameObject;
            if(SceneView.lastActiveSceneView!=null)SceneView.lastActiveSceneView.AlignViewToObject(rooms.visitor.viewCamera.transform);
        }
    }
}
