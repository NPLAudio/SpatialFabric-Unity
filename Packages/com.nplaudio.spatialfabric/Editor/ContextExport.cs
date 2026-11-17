// Copyright (c) 2026 SpatialFabric Contributors. MIT.
// Editor-only exporter for Path Studio scene context (Context v1). Reads the scene; never modifies it,
// never enters play mode, never touches audio/OSC/network.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NPLAudio.SpatialFabric.Editor
{
    public static class ContextExport
    {
        const string Title = "SpatialFabric Context Export";

        [MenuItem("Tools/SpatialFabric/Export Path Studio Context...")]
        public static void ExportMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            var stages = FindStages(scene);
            if (stages.Count == 0)
            {
                EditorUtility.DisplayDialog(Title, "The active scene has no SpatialStage. Add a SpatialStage so object positions can be converted into the canonical stage frame, then export again.", "OK");
                return;
            }
            SpatialStage stage = stages[0];
            ContextResult result = Gather(stage, scene, Selection.gameObjects);
            string defaultName = (string.IsNullOrEmpty(scene.name) ? "scene" : scene.name) + ".sfctx.json";
            string path = EditorUtility.SaveFilePanel("Export Path Studio Context", "", defaultName, "json");
            if (string.IsNullOrEmpty(path)) return;
            try { WriteAtomic(path, result.Json); }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(Title, "Could not write the file:\n" + e.Message, "OK");
                return;
            }
            ContextReport r = result.Report;
            string which = stages.Count > 1 ? "\nStage used: '" + HierarchyPath(stage.transform) + "' (first of " + stages.Count + " stages in the scene)." : "";
            EditorUtility.DisplayDialog(Title,
                "Exported " + r.Exported + " objects to\n" + path + which +
                "\nSkipped (non-finite): " + r.SkippedNonFinite +
                "\nClamped to range: " + r.Clamped +
                "\nTruncated id/label: " + r.TruncatedText +
                "\nDropped by 4096 cap: " + r.TruncatedByCap, "OK");
        }

        /// <summary>Stages in the scene in hierarchy order (active and enabled only).</summary>
        public static List<SpatialStage> FindStages(Scene scene)
        {
            var list = new List<SpatialStage>();
            if (!scene.IsValid() || !scene.isLoaded) return list;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (SpatialStage s in root.GetComponentsInChildren<SpatialStage>(false))
                    if (s.enabled) list.Add(s);
            return list;
        }

        /// <summary>Builds the context document for the scene using the authored stage frame (follow-listener mode ignored, no clamping to +/-1).</summary>
        public static ContextResult Gather(SpatialStage stage, Scene scene, GameObject[] selection)
        {
            var frame = new ContextStageFrame(stage.dimensionsMeters.x, stage.dimensionsMeters.y, stage.dimensionsMeters.z, stage.flipFront, stage.flipRight, stage.flipUp);
            var entries = new List<ContextEntry>();

            foreach (GameObject go in Candidates(scene, selection))
            {
                var corners = new List<(double x, double y, double z)>();
                foreach (Renderer r in go.GetComponents<Renderer>())
                {
                    if (!r.enabled) continue;
                    Matrix4x4 m = r.transform.localToWorldMatrix;
                    AddCorners(stage, corners, r.localBounds, m);
                }
                foreach (Collider c in go.GetComponents<Collider>())
                {
                    if (!c.enabled) continue;
                    AddCorners(stage, corners, c.bounds, Matrix4x4.identity);
                }
                if (corners.Count == 0) continue;
                var box = ContextExportCore.BoxFromLocalCorners(frame, corners);
                entries.Add(new ContextEntry { Id = HierarchyPath(go.transform), Label = go.name, Kind = ContextKind.Box, Position = box.center, HalfExtents = box.halfExtents });
            }

            Transform listener = stage.listener;
            if (listener != null)
            {
                Vector3 p = stage.transform.InverseTransformPoint(listener.position);
                Vector3 f = stage.transform.InverseTransformDirection(listener.forward);
                var c = ContextExportCore.ToCanonical(frame, p.x, p.y, p.z);
                entries.Add(new ContextEntry
                {
                    Id = HierarchyPath(listener) + ":listener", Label = listener.name, Kind = ContextKind.Listener,
                    Position = new[] { c.front, c.right, c.up },
                    YawDegrees = ContextExportCore.ListenerYawDegrees(frame, f.x, f.y, f.z)
                });
            }

            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (SpatialObject so in root.GetComponentsInChildren<SpatialObject>(false))
                {
                    Transform pose = so.Pose;
                    Vector3 p = stage.transform.InverseTransformPoint(pose.position);
                    var c = ContextExportCore.ToCanonical(frame, p.x, p.y, p.z);
                    entries.Add(new ContextEntry
                    {
                        Id = HierarchyPath(so.transform) + ":point", Label = so.DisplayName, Kind = ContextKind.Point,
                        Position = new[] { c.front, c.right, c.up }
                    });
                }

            return ContextExportCore.Build(scene.name, entries);
        }

        static void AddCorners(SpatialStage stage, List<(double x, double y, double z)> output, Bounds b, Matrix4x4 localToWorld)
        {
            Vector3 c = b.center, e = b.extents;
            for (int i = 0; i < 8; ++i)
            {
                var local = new Vector3(c.x + ((i & 1) == 0 ? -e.x : e.x), c.y + ((i & 2) == 0 ? -e.y : e.y), c.z + ((i & 4) == 0 ? -e.z : e.z));
                Vector3 inStage = stage.transform.InverseTransformPoint(localToWorld.MultiplyPoint3x4(local));
                output.Add((inStage.x, inStage.y, inStage.z));
            }
        }

        static IEnumerable<GameObject> Candidates(Scene scene, GameObject[] selection)
        {
            var seen = new HashSet<GameObject>();
            if (selection != null && selection.Length > 0)
            {
                foreach (GameObject sel in selection)
                {
                    if (sel == null || !sel.scene.IsValid() || sel.scene != scene) continue;
                    foreach (Transform t in sel.GetComponentsInChildren<Transform>(false))
                        if (Eligible(t.gameObject) && seen.Add(t.gameObject)) yield return t.gameObject;
                }
                yield break;
            }
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(false))
                    if (Eligible(t.gameObject)) yield return t.gameObject;
        }

        static bool Eligible(GameObject go)
        {
            if (!go.activeInHierarchy) return false;
            if (go.hideFlags != HideFlags.None && (go.hideFlags & (HideFlags.HideInHierarchy | HideFlags.HideInInspector | HideFlags.DontSaveInBuild)) != 0) return false;
            if (go.CompareTag("EditorOnly")) return false;
            if (go.GetComponent<RectTransform>() != null || go.GetComponent<CanvasRenderer>() != null || go.GetComponentInParent<Canvas>() != null) return false;
            foreach (Renderer r in go.GetComponents<Renderer>()) if (r.enabled) return true;
            foreach (Collider c in go.GetComponents<Collider>()) if (c.enabled) return true;
            return false;
        }

        public static string HierarchyPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (Transform p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        static void WriteAtomic(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }
}
