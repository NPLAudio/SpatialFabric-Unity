// Copyright (c) 2026 SpatialFabric Contributors. MIT.
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using NPLAudio.SpatialFabric.Paths;
namespace NPLAudio.SpatialFabric.Editor
{
    /// <summary>Tools → SpatialFabric → Import Path Studio Path...: validates a Path v1 file and stores it as a SpatialPathAsset.</summary>
    public static class SpatialPathImporter
    {
        const string MenuPath = "Tools/SpatialFabric/Import Path Studio Path...";

        /// <summary>Validate file bytes. Returns the UTF-8 text when valid; otherwise null and a readable message.</summary>
        public static string ValidateBytes(byte[] bytes, out string error)
        {
            error = null;
            JsonNode root;
            try { root = PathJson.Parse(bytes); }
            catch (FormatException e) { error = e.Message; return null; }
            var doc = PathDocument.TryLoad(root, out var problems);
            if (doc == null)
            {
                var text = new StringBuilder("Path file is not a valid Path v1 document:");
                for (int i = 0; i < problems.Count && i < 12; ++i) text.Append("\n  ").Append(problems[i].Code).Append("  ").Append(problems[i].Path);
                if (problems.Count > 12) text.Append("\n  ... ").Append(problems.Count - 12).Append(" more");
                error = text.ToString();
                return null;
            }
            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return new UTF8Encoding(false, true).GetString(bytes, start, bytes.Length - start);
        }

        /// <summary>Create the asset at an Assets-relative path. Returns null (and a message) if the file is invalid.</summary>
        public static SpatialPathAsset CreateAsset(byte[] bytes, string assetPath, out string error)
        {
            string text = ValidateBytes(bytes, out error);
            if (text == null) return null;
            var asset = ScriptableObject.CreateInstance<SpatialPathAsset>();
            asset.SetJson(text);
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        [MenuItem(MenuPath)]
        static void Import()
        {
            string file = EditorUtility.OpenFilePanel("Import Path Studio path", "", "json");
            if (string.IsNullOrEmpty(file)) return;
            byte[] bytes;
            try { bytes = File.ReadAllBytes(file); }
            catch (Exception e) { EditorUtility.DisplayDialog("Import Path Studio Path", "Cannot read file:\n" + e.Message, "OK"); return; }
            string error;
            if (ValidateBytes(bytes, out error) == null) { EditorUtility.DisplayDialog("Import Path Studio Path", error, "OK"); return; }
            string name = Path.GetFileName(file);
            foreach (string suffix in new[] { ".sfpath.json", ".json" })
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { name = name.Substring(0, name.Length - suffix.Length); break; }
            string assetPath = EditorUtility.SaveFilePanelInProject("Save path asset", name, "asset", "Choose where to store the baked path asset.");
            if (string.IsNullOrEmpty(assetPath)) return;
            var asset = CreateAsset(bytes, assetPath, out error);
            if (asset == null) { EditorUtility.DisplayDialog("Import Path Studio Path", error, "OK"); return; }
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
    }
}
