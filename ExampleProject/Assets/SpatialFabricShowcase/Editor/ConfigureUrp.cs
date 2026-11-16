// Explicit, owned-assets-only migration. No networking, PCM preparation or playback. MIT.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NPLAudio.SpatialFabric.Example.Editor
{
    public static class ConfigureUrp
    {
        const string Root = "Assets/SpatialFabricShowcase";
        const string Folder = Root + "/Rendering";
        public const string PipelinePath = Folder + "/ShowcaseURP.asset";

        [MenuItem("Tools/SpatialFabric/Configure example URP (owned assets only)")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play before changing rendering.");
            for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save or discard scene edits explicitly first.");
            var previous = GraphicsSettings.defaultRenderPipeline;
            if (previous != null && AssetDatabase.GetAssetPath(previous) != PipelinePath)
                throw new InvalidOperationException("Refusing to replace an unrelated render pipeline.");
            for(int i=0;i<QualitySettings.names.Length;i++)
            {
                var quality=QualitySettings.GetRenderPipelineAssetAt(i);
                if(quality!=null && AssetDatabase.GetAssetPath(quality)!=PipelinePath)
                    throw new InvalidOperationException("Refusing to replace an unrelated quality pipeline.");
            }
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            string rendererPath=Folder+"/ShowcaseRenderer.asset";
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if(renderer==null){renderer=ScriptableObject.CreateInstance<UniversalRendererData>();AssetDatabase.CreateAsset(renderer,rendererPath);}
            renderer.renderingMode=RenderingMode.ForwardPlus;EditorUtility.SetDirty(renderer);
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if(pipeline==null){pipeline=UniversalRenderPipelineAsset.Create(renderer);AssetDatabase.CreateAsset(pipeline,PipelinePath);}
            pipeline.msaaSampleCount=4;pipeline.supportsHDR=true;pipeline.maxAdditionalLightsCount=8;
            EditorUtility.SetDirty(pipeline);
            GraphicsSettings.defaultRenderPipeline=pipeline;
            int originalQuality=QualitySettings.GetQualityLevel();
            try{for(int i=0;i<QualitySettings.names.Length;i++){QualitySettings.SetQualityLevel(i,false);QualitySettings.renderPipeline=pipeline;}}
            finally{QualitySettings.SetQualityLevel(originalQuality,false);}
            var shader=Shader.Find("Universal Render Pipeline/Lit");
            if(shader==null)throw new InvalidOperationException("URP Lit shader missing; install the example's URP dependency.");
            foreach(var guid in AssetDatabase.FindAssets("t:Material",new[]{Root+"/GeneratedRooms"}))
            {
                var material=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if(material.name=="RoomText")continue;
                Color color=material.color;material.shader=shader;material.color=color;material.SetFloat("_Smoothness",.05f);EditorUtility.SetDirty(material);
            }
            // The small protocol-regression scene used Unity's Built-in default primitive material.
            // Touch only that original scene, never rebuild rooms or replace identities/audio/actors.
            var setup=EditorSceneManager.GetSceneManagerSetup();
            try
            {
                string path=Root+"/Scenes/SpatialExample.unity";
                if(File.Exists(path))
                {
                    var scene=EditorSceneManager.OpenScene(path);
                    var fallback=AssetDatabase.LoadAssetAtPath<Material>(Root+"/GeneratedRooms/RoomWall.mat");
                    foreach(var root in scene.GetRootGameObjects())foreach(var mesh in root.GetComponentsInChildren<Renderer>(true))
                    {
                        var materials=mesh.sharedMaterials;bool changed=false;
                        for(int i=0;i<materials.Length;i++)if(materials[i]!=null && materials[i].shader.name=="Standard") {materials[i]=fallback;changed=true;}
                        if(changed)mesh.sharedMaterials=materials;
                    }
                    EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
                }
                if(File.Exists(ShowcaseRooms.RoomScenePath))
                {
                    var scene=EditorSceneManager.OpenScene(ShowcaseRooms.RoomScenePath);
                    foreach(var root in scene.GetRootGameObjects())foreach(var light in root.GetComponentsInChildren<Light>(true))
                    {
                        // Absolute values make repeated migrations idempotent. URP uses inverse-square attenuation.
                        if(light.name.StartsWith("Spatial fill "))light.intensity=36f;
                        else if(light.name.StartsWith("Room fill "))light.intensity=10f;
                        else if(light.name.StartsWith("Hall fill "))light.intensity=8f;
                    }
                    EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
                }
            }
            finally
            {
                if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("SpatialFabric example: URP Forward+, all quality levels and owned room materials configured; no transport/audio started.");
        }
    }
}
