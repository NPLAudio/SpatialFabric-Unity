// Copyright (c) 2026 SpatialFabric Contributors. MIT.
// Pure C# (System only). Typed Path v1 model built only from documents that passed PathValidator.
using System;
using System.Collections.Generic;

namespace NPLAudio.SpatialFabric.Paths
{
    public enum PathLoop { None, Loop, PingPong }
    public enum PathInterp { Linear, Hold, CatmullRom, Bezier }
    public enum PathEase { Linear, In, Out, InOut }

    public sealed class PathKeyframe
    {
        public double T;
        public double[] Position = new double[3]; // canonical [front, right, up], waypoint already resolved
        public string Waypoint;                   // original reference, informational
        public PathInterp Interp;
        public PathEase Ease;
        public double[] TangentIn = new double[3], TangentOut = new double[3];
    }

    public struct PathCurvePoint
    {
        public double T, Value;
    }

    public sealed class PathTrack
    {
        public string Id, TargetKind, TargetKey, TargetLabel;
        public long TargetObjectId; // 0 when absent
        public PathLoop Loop;
        public double Speed = 1;
        public PathKeyframe[] Keyframes;
        public PathCurvePoint[] Gain, Spread; // null when absent

        /// <summary>Wall-clock seconds until the last keyframe is reached once (keyframe span end / speed).</summary>
        public double Duration => Keyframes[Keyframes.Length - 1].T / Speed;
    }

    public sealed class PathDocument
    {
        public string Name = "";
        public PathTrack[] Tracks = new PathTrack[0];

        public PathTrack FindTrack(string id)
        {
            foreach (var t in Tracks) if (t.Id == id) return t;
            return null;
        }

        /// <summary>
        /// Parse + validate. Returns the document, or null with Problems filled. Unparseable text is reported
        /// as one "bad_json" problem (not a Path v1 code; the text never became a document).
        /// </summary>
        public static PathDocument TryLoad(string json, out List<PathProblem> problems)
        {
            JsonNode root;
            try { root = PathJson.Parse(json); }
            catch (FormatException e) { problems = new List<PathProblem> { new PathProblem("bad_json", e.Message) }; return null; }
            return TryLoad(root, out problems);
        }

        public static PathDocument TryLoad(JsonNode root, out List<PathProblem> problems)
        {
            problems = PathValidator.Validate(root);
            return problems.Count == 0 ? Build(root) : null;
        }

        static PathDocument Build(JsonNode root)
        {
            var doc = new PathDocument();
            var name = root.Get("name");
            if (name != null && name.Kind == JsonKind.String) doc.Name = name.String;
            var waypoints = new Dictionary<string, double[]>(StringComparer.Ordinal);
            var rawWaypoints = root.Get("waypoints");
            if (rawWaypoints != null)
                foreach (var w in rawWaypoints.Items) waypoints[w.Get("id").String] = Vec(w.Get("position"), null);
            var tracks = root.Get("tracks").Items;
            doc.Tracks = new PathTrack[tracks.Count];
            for (int i = 0; i < tracks.Count; ++i)
            {
                var jt = tracks[i];
                var target = jt.Get("target");
                var track = new PathTrack { Id = jt.Get("id").String, TargetKind = target.Get("kind").String, TargetKey = target.Get("key").String };
                if (target.Get("label") != null) track.TargetLabel = target.Get("label").String;
                if (target.Get("object_id") != null) track.TargetObjectId = (long)Math.Min(target.Get("object_id").Number, long.MaxValue);
                var loop = jt.Get("loop");
                track.Loop = loop == null || loop.String == "none" ? PathLoop.None : loop.String == "loop" ? PathLoop.Loop : PathLoop.PingPong;
                if (jt.Get("speed") != null) track.Speed = jt.Get("speed").Number;
                var keys = jt.Get("keyframes").Items;
                track.Keyframes = new PathKeyframe[keys.Count];
                for (int k = 0; k < keys.Count; ++k)
                {
                    var jk = keys[k];
                    var key = new PathKeyframe { T = jk.Get("t").Number };
                    if (jk.Has("waypoint")) { key.Waypoint = jk.Get("waypoint").String; key.Position = (double[])waypoints[key.Waypoint].Clone(); }
                    else key.Position = Vec(jk.Get("position"), null);
                    key.Interp = Pick(jk.Get("interp"), "hold", PathInterp.Hold, "catmull_rom", PathInterp.CatmullRom, "bezier", PathInterp.Bezier, PathInterp.Linear);
                    key.Ease = Pick(jk.Get("ease"), "in", PathEase.In, "out", PathEase.Out, "in_out", PathEase.InOut, PathEase.Linear);
                    key.TangentIn = Vec(jk.Get("tangent_in"), new double[3]);
                    key.TangentOut = Vec(jk.Get("tangent_out"), new double[3]);
                    track.Keyframes[k] = key;
                }
                var parameters = jt.Get("parameters");
                if (parameters != null)
                {
                    track.Gain = Curve(parameters.Get("gain"));
                    track.Spread = Curve(parameters.Get("spread"));
                }
                doc.Tracks[i] = track;
            }
            return doc;
        }

        static T Pick<T>(JsonNode n, string a, T av, string b, T bv, string c, T cv, T fallback)
        {
            if (n == null) return fallback;
            return n.String == a ? av : n.String == b ? bv : n.String == c ? cv : fallback;
        }

        static double[] Vec(JsonNode n, double[] fallback)
        {
            if (n == null) return fallback;
            return new[] { n.Items[0].Number, n.Items[1].Number, n.Items[2].Number };
        }

        static PathCurvePoint[] Curve(JsonNode n)
        {
            if (n == null) return null;
            var points = new PathCurvePoint[n.Items.Count];
            for (int i = 0; i < points.Length; ++i) points[i] = new PathCurvePoint { T = n.Items[i].Get("t").Number, Value = n.Items[i].Get("value").Number };
            return points;
        }
    }
}
