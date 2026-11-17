// Copyright (c) 2026 SpatialFabric Contributors. MIT.
// Pure C# (System only). Mirrors validate() in SpatialFabric Apps/PathStudio/pathstudio/model.py
// (Specs/Path-v1.md): reports every applicable code, never throws on malformed documents.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace NPLAudio.SpatialFabric.Paths
{
    public readonly struct PathProblem
    {
        public readonly string Code, Path;
        public PathProblem(string code, string path) { Code = code; Path = path; }
        public override string ToString() => Code + " at " + Path;
    }

    public static class PathValidator
    {
        public const string Schema = "org.nplaudio.spatialfabric.path";
        public const int MaxTracks = 256, MaxPoints = 10000, MaxWaypoints = 4096;
        public const double PositionLimit = 16, TangentLimit = 32, MaxSpeed = 64;

        static readonly string[] Kinds = { "object", "listener", "camera" };
        static readonly string[] Loops = { "none", "loop", "ping_pong" };
        static readonly string[] Interps = { "linear", "hold", "catmull_rom", "bezier" };
        static readonly string[] Eases = { "linear", "in", "out", "in_out" };

        /// <summary>Validate a parsed document. Empty result means valid. Sorted by code then path, de-duplicated.</summary>
        public static List<PathProblem> Validate(JsonNode document)
        {
            var errors = new HashSet<(string, string)>();
            try { Run(document, errors); }
            catch (Exception) { errors.Add(("bad_schema", "$")); } // defensive: malformed input must never throw
            var list = new List<PathProblem>();
            foreach (var e in errors) list.Add(new PathProblem(e.Item1, e.Item2));
            list.Sort((a, b) => { int c = string.CompareOrdinal(a.Code, b.Code); return c != 0 ? c : string.CompareOrdinal(a.Path, b.Path); });
            return list;
        }

        /// <summary>Parse then validate; unparseable text yields a single bad_schema problem at "$".</summary>
        public static List<PathProblem> Validate(string json)
        {
            JsonNode root;
            try { root = PathJson.Parse(json); }
            catch (FormatException) { return new List<PathProblem> { new PathProblem("bad_schema", "$") }; }
            return Validate(root);
        }

        public static bool IsId(string s)
        {
            if (s == null || s.Length < 1 || s.Length > 64) return false;
            foreach (char c in s)
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-')) return false;
            return true;
        }

        internal static bool IsNum(JsonNode n) => n != null && n.Kind == JsonKind.Number && !double.IsNaN(n.Number) && !double.IsInfinity(n.Number);

        internal static int CodePoints(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; ++i) { if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) ++i; ++n; }
            return n;
        }

        // shaped: array of exactly 3 finite numbers; ranged: shaped and every |v| <= limit.
        internal static void Vector(JsonNode v, double limit, out bool shaped, out bool ranged)
        {
            shaped = v != null && v.Kind == JsonKind.Array && v.Items.Count == 3 && IsNum(v.Items[0]) && IsNum(v.Items[1]) && IsNum(v.Items[2]);
            ranged = shaped && Math.Abs(v.Items[0].Number) <= limit && Math.Abs(v.Items[1].Number) <= limit && Math.Abs(v.Items[2].Number) <= limit;
        }

        static bool In(JsonNode v, string[] allowed, string fallback)
        {
            if (v == null) return Array.IndexOf(allowed, fallback) >= 0; // key absent -> default
            return v.Kind == JsonKind.String && Array.IndexOf(allowed, v.String) >= 0;
        }

        static void Run(JsonNode document, HashSet<(string, string)> errors)
        {
            Action<string, string> add = (code, path) => errors.Add((code, path));
            if (document == null || document.Kind != JsonKind.Object) { add("bad_schema", "$"); return; }
            var schema = document.Get("schema");
            if (schema == null || schema.Kind != JsonKind.String || schema.String != Schema) add("bad_schema", "$.schema");
            var version = document.Get("schema_version");
            if (version == null || version.Kind != JsonKind.Number || version.Number != 1) add("bad_version", "$.schema_version");
            var space = document.Get("space");
            if (space == null || space.Kind != JsonKind.String || space.String != "canonical") add("bad_space", "$.space");

            var waypoints = new HashSet<string>(StringComparer.Ordinal);
            var rawWaypoints = document.Has("waypoints") ? document.Get("waypoints") : new JsonNode { Kind = JsonKind.Array, Items = new List<JsonNode>() };
            if (rawWaypoints.Kind != JsonKind.Array || rawWaypoints.Items.Count > MaxWaypoints)
            {
                add("bad_id", "$.waypoints");
                rawWaypoints = new JsonNode { Kind = JsonKind.Array, Items = new List<JsonNode>() };
            }
            for (int index = 0; index < rawWaypoints.Items.Count; ++index)
            {
                var wp = rawWaypoints.Items[index];
                string where = "$.waypoints[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                var id = wp.Get("id");
                if (wp.Kind != JsonKind.Object || id == null || id.Kind != JsonKind.String || !IsId(id.String)) { add("bad_id", where + ".id"); continue; }
                if (waypoints.Contains(id.String)) add("duplicate_id", where + ".id");
                Vector(wp.Get("position"), PositionLimit, out bool shaped, out bool ranged);
                if (!shaped) add("bad_position", where + ".position");
                else if (!ranged) add("out_of_range", where + ".position");
                else waypoints.Add(id.String);
            }

            var tracks = document.Get("tracks");
            List<JsonNode> trackList = null;
            if (tracks == null || tracks.Kind != JsonKind.Array || tracks.Items.Count == 0) add("no_tracks", "$.tracks");
            else if (tracks.Items.Count > MaxTracks) add("too_many_tracks", "$.tracks");
            else trackList = tracks.Items;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (trackList != null)
                for (int ti = 0; ti < trackList.Count; ++ti)
                    ValidateTrack(trackList[ti], "$.tracks[" + ti.ToString(CultureInfo.InvariantCulture) + "]", waypoints, seen, add);
        }

        static void ValidateTrack(JsonNode track, string where, HashSet<string> waypoints, HashSet<string> seen, Action<string, string> add)
        {
            if (track.Kind != JsonKind.Object) { add("bad_target", where); return; }
            var tid = track.Get("id");
            if (tid == null || tid.Kind != JsonKind.String || !IsId(tid.String)) add("bad_id", where + ".id");
            else if (seen.Contains(tid.String)) add("duplicate_id", where + ".id");
            else seen.Add(tid.String);

            var target = track.Get("target");
            var key = target?.Get("key");
            if (target == null || target.Kind != JsonKind.Object || !In(target.Get("kind"), Kinds, null)
                || key == null || key.Kind != JsonKind.String || CodePoints(key.String) < 1 || CodePoints(key.String) > 256)
                add("bad_target", where + ".target");
            else if (!OptionalTargetFieldsOk(target)) add("bad_target", where + ".target");

            if (!In(track.Get("loop"), Loops, "none")) add("bad_loop", where + ".loop");
            var speed = track.Get("speed");
            if (speed != null && !(IsNum(speed) && speed.Number > 0 && speed.Number <= MaxSpeed)) add("bad_speed", where + ".speed");

            var keys = track.Get("keyframes");
            List<JsonNode> keyList = null;
            if (keys == null || keys.Kind != JsonKind.Array || keys.Items.Count == 0) add("no_keyframes", where + ".keyframes");
            else if (keys.Items.Count > MaxPoints) add("too_many_keyframes", where + ".keyframes");
            else keyList = keys.Items;
            if (keyList != null)
            {
                double previous = 0; bool hasPrevious = false;
                for (int ki = 0; ki < keyList.Count; ++ki)
                {
                    var frame = keyList[ki];
                    string kw = where + ".keyframes[" + ki.ToString(CultureInfo.InvariantCulture) + "]";
                    if (frame.Kind != JsonKind.Object) { add("bad_time", kw); continue; }
                    var t = frame.Get("t");
                    if (!IsNum(t) || t.Number < 0) add("bad_time", kw + ".t");
                    else
                    {
                        if (hasPrevious && t.Number <= previous) add("times_not_increasing", kw + ".t");
                        previous = t.Number; hasPrevious = true;
                    }
                    bool hasPosition = frame.Has("position"), hasWaypoint = frame.Has("waypoint");
                    if (hasPosition == hasWaypoint) add("position_conflict", kw);
                    else if (hasPosition)
                    {
                        Vector(frame.Get("position"), PositionLimit, out bool shaped, out bool ranged);
                        if (!shaped) add("bad_position", kw + ".position");
                        else if (!ranged) add("out_of_range", kw + ".position");
                    }
                    else
                    {
                        var wp = frame.Get("waypoint");
                        if (wp.Kind != JsonKind.String || !waypoints.Contains(wp.String)) add("unknown_waypoint", kw + ".waypoint");
                    }
                    if (!In(frame.Get("interp"), Interps, "linear")) add("bad_interp", kw + ".interp");
                    if (!In(frame.Get("ease"), Eases, "linear")) add("bad_ease", kw + ".ease");
                    foreach (string name in new[] { "tangent_in", "tangent_out" })
                    {
                        if (!frame.Has(name)) continue;
                        Vector(frame.Get(name), TangentLimit, out bool shaped, out bool ranged);
                        if (!ranged) add("bad_tangent", kw + "." + name);
                    }
                }
            }

            var parameters = track.Get("parameters");
            if (parameters == null) return;
            if (parameters.Kind != JsonKind.Object) { add("bad_parameter", where + ".parameters"); return; }
            ValidateCurve(parameters, "gain", 0, 4, where, add);
            ValidateCurve(parameters, "spread", 0, 1, where, add);
        }

        static bool OptionalTargetFieldsOk(JsonNode target)
        {
            var label = target.Get("label");
            if (label != null && (label.Kind != JsonKind.String || CodePoints(label.String) > 256)) return false;
            var objectId = target.Get("object_id");
            return objectId == null || objectId.Kind == JsonKind.Number && objectId.IsInteger && objectId.Number >= 1;
        }

        static void ValidateCurve(JsonNode parameters, string name, double low, double high, string where, Action<string, string> add)
        {
            var curve = parameters.Get(name);
            if (curve == null) return;
            string pw = where + ".parameters." + name;
            if (curve.Kind != JsonKind.Array || curve.Items.Count < 1 || curve.Items.Count > MaxPoints) { add("bad_parameter", pw); return; }
            double last = 0; bool hasLast = false;
            foreach (var point in curve.Items)
            {
                var t = point.Get("t"); var value = point.Get("value");
                if (point.Kind != JsonKind.Object || !IsNum(t) || t.Number < 0 || !IsNum(value) || !(value.Number >= low && value.Number <= high)
                    || hasLast && t.Number <= last)
                { add("bad_parameter", pw); return; }
                last = t.Number; hasLast = true;
            }
        }
    }
}
