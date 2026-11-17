// Copyright (c) 2026 SpatialFabric Contributors. MIT.
using System;
using System.Collections.Generic;
using UnityEngine;
using NPLAudio.SpatialFabric.Paths;
namespace NPLAudio.SpatialFabric
{
    /// <summary>Baked Path Studio path (Path v1). Keeps the original JSON text; parsed and validated lazily.</summary>
    [CreateAssetMenu(fileName = "NewSpatialPath", menuName = "SpatialFabric/Spatial Path")]
    public sealed class SpatialPathAsset : ScriptableObject
    {
        [SerializeField, TextArea(4, 12)] string json = "";
        PathDocument document;
        List<PathProblem> problems;
        bool parsed;

        public string Json => json;
        public void SetJson(string text) { json = text ?? ""; Invalidate(); }
        void OnValidate() => Invalidate();
        void OnEnable() => Invalidate();
        void Invalidate() { parsed = false; document = null; problems = null; }

        void EnsureParsed()
        {
            if (parsed) return;
            parsed = true;
            document = PathDocument.TryLoad(json, out problems);
        }

        /// <summary>The validated document, or null when the JSON is invalid (see Problems).</summary>
        public PathDocument Document { get { EnsureParsed(); return document; } }
        /// <summary>Validation problems (code + document path). Empty when valid. Unparseable JSON reports "bad_json".</summary>
        public IReadOnlyList<PathProblem> Problems { get { EnsureParsed(); return problems; } }
        public bool IsValid => Document != null;

        public string[] TrackIds
        {
            get
            {
                var doc = Document;
                if (doc == null) return new string[0];
                var ids = new string[doc.Tracks.Length];
                for (int i = 0; i < ids.Length; ++i) ids[i] = doc.Tracks[i].Id;
                return ids;
            }
        }

        public string ProblemSummary(int max = 8)
        {
            var list = Problems; var text = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count && i < max; ++i) text.Append(i > 0 ? "; " : "").Append(list[i].ToString());
            if (list.Count > max) text.Append("; ... (").Append(list.Count - max).Append(" more)");
            return text.ToString();
        }

        /// <summary>Sample a track at wall time (seconds); the track's loop/speed are applied. Throws if the asset is invalid or the track unknown.</summary>
        public PathSample Sample(string trackId, double time)
        {
            var doc = Document;
            if (doc == null) throw new InvalidOperationException("Path asset is invalid: " + ProblemSummary());
            return PathSampler.Sample(doc, trackId, time);
        }
    }
}
