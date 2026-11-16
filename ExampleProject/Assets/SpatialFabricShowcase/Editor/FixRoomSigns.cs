// Focused, backed-up visual update; geometry, IDs, audio and routing are untouched. MIT.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace NPLAudio.SpatialFabric.Example.Editor
{
    public static class FixRoomSigns
    {
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-spatialFabricEvidence");if(i<0 || i+1>=args.Length)throw new ArgumentException("Evidence folder required");
            EditorSceneManager.OpenScene(ShowcaseRooms.RoomScenePath);var rooms=UnityObjects.FindAll<ShowcaseRooms>()[0];
            if(rooms.visualVersion>=1)return;
            string backup=Path.Combine(args[i+1],"BeforeVisualFix");Directory.CreateDirectory(backup);File.Copy(ShowcaseRooms.RoomScenePath,Path.Combine(backup,"RoomShowcase.unity"),false);
            foreach(var file in Directory.GetFiles("Assets/SpatialFabricShowcase/GeneratedRooms","*.mat"))File.Copy(file,Path.Combine(backup,Path.GetFileName(file)),false);
            foreach(var label in rooms.GetComponentsInChildren<TextMesh>(true))
            {
                label.characterSize*=.1f;label.GetComponent<MeshRenderer>().sharedMaterial=BuildRoomShowcase.TextMaterial(label.font);if(label.GetComponent<RoomSign>()==null)label.gameObject.AddComponent<RoomSign>();EditorUtility.SetDirty(label);
            }
            foreach(var file in Directory.GetFiles("Assets/SpatialFabricShowcase/GeneratedRooms","*.mat"))
            {
                var m=AssetDatabase.LoadAssetAtPath<Material>(file.Replace('\\','/'));if(m!=null && m.HasProperty("_Glossiness")){m.SetFloat("_Glossiness",.05f);EditorUtility.SetDirty(m);}
            }
            rooms.visualVersion=1;EditorUtility.SetDirty(rooms);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(rooms.gameObject.scene);
        }
    }
}
