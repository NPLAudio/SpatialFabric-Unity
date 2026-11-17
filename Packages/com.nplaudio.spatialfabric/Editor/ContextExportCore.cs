// Copyright (c) 2026 SpatialFabric Contributors. MIT.
// Pure C# (System only) so Tools/CoreChecks can compile and test it without Unity.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NPLAudio.SpatialFabric.Editor
{
    /// <summary>Authored stage frame: full dimensions in canonical front/right/up meters plus axis flips.</summary>
    public struct ContextStageFrame
    {
        public double DimFront, DimRight, DimUp;
        public bool FlipFront, FlipRight, FlipUp;
        public ContextStageFrame(double front, double right, double up, bool flipFront = false, bool flipRight = false, bool flipUp = false)
        {
            DimFront = front; DimRight = right; DimUp = up; FlipFront = flipFront; FlipRight = flipRight; FlipUp = flipUp;
        }
    }

    public enum ContextKind { Box, Sphere, Point, Listener }

    public sealed class ContextEntry
    {
        public string Id = "", Label = "";
        public ContextKind Kind;
        public double[] Position = new double[3];   // canonical [front, right, up]
        public double[] HalfExtents = new double[3]; // box only
        public double Radius;                        // sphere only
        public double YawDegrees;                    // listener only
    }

    public sealed class ContextReport
    {
        public int Exported, SkippedNonFinite, Clamped, TruncatedByCap, TruncatedText;
    }

    public sealed class ContextResult
    {
        public string Json = "";
        public ContextReport Report = new ContextReport();
    }

    public static class ContextExportCore
    {
        public const int MaxObjects = 4096, MaxId = 128, MaxLabel = 256;
        public const double MaxPosition = 64, MaxExtent = 64, MinHalfExtentMeters = .05;

        static double Half(double dim) => Math.Max(MinHalfExtentMeters, dim * .5);

        /// <summary>Stage-local Unity axes (x=right, y=up, z=front) to canonical (front,right,up). Unclamped.</summary>
        public static (double front, double right, double up) ToCanonical(ContextStageFrame f, double x, double y, double z)
        {
            return (z / Half(f.DimFront) * (f.FlipFront ? -1 : 1),
                x / Half(f.DimRight) * (f.FlipRight ? -1 : 1),
                y / Half(f.DimUp) * (f.FlipUp ? -1 : 1));
        }

        /// <summary>Conservative canonical AABB of points given in stage-local Unity coordinates.</summary>
        public static (double[] center, double[] halfExtents) BoxFromLocalCorners(ContextStageFrame f, IReadOnlyList<(double x, double y, double z)> corners)
        {
            if (corners == null || corners.Count == 0) throw new ArgumentException("corners required");
            double[] lo = { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
            double[] hi = { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
            foreach (var c in corners)
            {
                var p = ToCanonical(f, c.x, c.y, c.z);
                double[] v = { p.front, p.right, p.up };
                for (int i = 0; i < 3; ++i) { lo[i] = Math.Min(lo[i], v[i]); hi[i] = Math.Max(hi[i], v[i]); }
            }
            var center = new double[3]; var half = new double[3];
            for (int i = 0; i < 3; ++i) { center[i] = (lo[i] + hi[i]) * .5; half[i] = (hi[i] - lo[i]) * .5; }
            return (center, half);
        }

        /// <summary>Yaw of a stage-local forward vector: 0 faces +front, positive turns toward the left (-right).</summary>
        public static double ListenerYawDegrees(ContextStageFrame f, double fx, double fy, double fz)
        {
            double front = fz * (f.FlipFront ? -1 : 1), right = fx * (f.FlipRight ? -1 : 1);
            if (Math.Abs(front) < 1e-9 && Math.Abs(right) < 1e-9) return 0;
            double yaw = Math.Atan2(0.0 - right, front) * 180.0 / Math.PI;
            return yaw == 0 ? 0 : yaw; // normalize -0
        }

        public static string SanitizeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "object";
            var sb = new StringBuilder(id.Length);
            foreach (char c in id)
            {
                bool ok = c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_' || c == '.' || c == '/' || c == ':' || c == '-';
                sb.Append(ok ? c : '_');
            }
            return sb.ToString();
        }

        static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        static bool Clamp(ref double v, double lo, double hi)
        {
            double c = Math.Min(hi, Math.Max(lo, v));
            bool changed = c != v; v = c; return changed;
        }

        static string Truncate(string s, int max, ref bool truncated)
        {
            if (s.Length <= max) return s;
            truncated = true;
            if (char.IsHighSurrogate(s[max - 1])) --max;
            return s.Substring(0, max);
        }

        public static ContextResult Build(string name, IEnumerable<ContextEntry> entries)
        {
            var result = new ContextResult(); var report = result.Report;
            var used = new HashSet<string>(StringComparer.Ordinal);
            var rows = new List<string>();
            foreach (var e in entries ?? new ContextEntry[0])
            {
                if (e == null) continue;
                bool bad = e.Position == null || e.Position.Length != 3 || !Finite(e.Position[0]) || !Finite(e.Position[1]) || !Finite(e.Position[2]);
                if (!bad && e.Kind == ContextKind.Box) bad = e.HalfExtents == null || e.HalfExtents.Length != 3 || !Finite(e.HalfExtents[0]) || !Finite(e.HalfExtents[1]) || !Finite(e.HalfExtents[2]);
                if (!bad && e.Kind == ContextKind.Sphere) bad = !Finite(e.Radius);
                if (!bad && e.Kind == ContextKind.Listener) bad = !Finite(e.YawDegrees);
                if (bad) { ++report.SkippedNonFinite; continue; }
                if (report.Exported >= MaxObjects) { ++report.TruncatedByCap; continue; }

                bool clamped = false, cut = false;
                string id = Truncate(SanitizeId(e.Id), MaxId, ref cut);
                if (!used.Add(id))
                {
                    for (int n = 2; ; ++n)
                    {
                        string suffix = "-" + n.ToString(CultureInfo.InvariantCulture);
                        string cand = (id.Length + suffix.Length > MaxId ? id.Substring(0, MaxId - suffix.Length) : id) + suffix;
                        if (used.Add(cand)) { id = cand; break; }
                    }
                }
                string label = Truncate(e.Label ?? "", MaxLabel, ref cut);
                var pos = new[] { e.Position[0], e.Position[1], e.Position[2] };
                for (int i = 0; i < 3; ++i) clamped |= Clamp(ref pos[i], -MaxPosition, MaxPosition);

                var sb = new StringBuilder();
                sb.Append("    {\n      \"id\": ").Append(Quote(id));
                sb.Append(",\n      \"kind\": \"").Append(e.Kind.ToString().ToLowerInvariant()).Append('"');
                if (label.Length > 0) sb.Append(",\n      \"label\": ").Append(Quote(label));
                sb.Append(",\n      \"position\": ").Append(Vec(pos));
                if (e.Kind == ContextKind.Box)
                {
                    var h = new[] { e.HalfExtents[0], e.HalfExtents[1], e.HalfExtents[2] };
                    for (int i = 0; i < 3; ++i) clamped |= Clamp(ref h[i], 0, MaxExtent);
                    sb.Append(",\n      \"half_extents\": ").Append(Vec(h));
                }
                else if (e.Kind == ContextKind.Sphere)
                {
                    double r = e.Radius; clamped |= Clamp(ref r, 0, MaxExtent);
                    sb.Append(",\n      \"radius\": ").Append(Num(r));
                }
                else if (e.Kind == ContextKind.Listener)
                {
                    double y = e.YawDegrees; clamped |= Clamp(ref y, -360, 360);
                    sb.Append(",\n      \"yaw_degrees\": ").Append(Num(y));
                }
                sb.Append("\n    }");
                rows.Add(sb.ToString());
                if (clamped) ++report.Clamped;
                if (cut) ++report.TruncatedText;
                ++report.Exported;
            }

            var doc = new StringBuilder();
            doc.Append("{\n  \"schema\": \"org.nplaudio.spatialfabric.context\",\n  \"schema_version\": 1,\n  \"space\": \"canonical\"");
            bool ignored = false;
            string docName = Truncate(name ?? "", MaxLabel, ref ignored);
            if (docName.Length > 0) doc.Append(",\n  \"name\": ").Append(Quote(docName));
            if (rows.Count == 0) doc.Append(",\n  \"objects\": []\n}\n");
            else doc.Append(",\n  \"objects\": [\n").Append(string.Join(",\n", rows)).Append("\n  ]\n}\n");
            result.Json = doc.ToString();
            return result;
        }

        static string Num(double v)
        {
            v = Math.Round(v, 6);
            if (v == 0) return "0";
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        static string Vec(double[] v) => "[" + Num(v[0]) + ", " + Num(v[1]) + ", " + Num(v[2]) + "]";

        public static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2); sb.Append('"');
            for (int i = 0; i < s.Length; ++i)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[++i]); }
                        else if (char.IsSurrogate(c)) sb.Append('\uFFFD');
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
