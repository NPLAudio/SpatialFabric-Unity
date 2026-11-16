// Explicit silent scene audit. Does not save transient walking test positions. MIT.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace NPLAudio.SpatialFabric.Example.Editor
{
    public static class ValidateRoomShowcase
    {
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-spatialFabricEvidence");if(i<0 || i+1>=args.Length)throw new ArgumentException("Evidence directory required");
            EditorSceneManager.OpenScene(ShowcaseRooms.RoomScenePath);var rooms=UnityObjects.FindAll<ShowcaseRooms>()[0];
            var report=RoomValidation.Validate(rooms,true);Directory.CreateDirectory(args[i+1]);File.WriteAllText(Path.Combine(args[i+1],"room-editor.json"),JsonUtility.ToJson(report,true));
            if(report.failed.Count!=0)throw new InvalidOperationException(string.Join("; ",report.failed));
            Debug.Log("Room showcase silent checks passed: "+report.passed.Count);
        }
    }
}
