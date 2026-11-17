// Copyright (c) 2026 SpatialFabric Contributors. MIT.
// Pure C# (System only). Deterministic sampler matching sample() in the SpatialFabric Python reference
// (Specs/Path-v1.md, "Sampling"); IEEE double arithmetic.
using System;

namespace NPLAudio.SpatialFabric.Paths
{
    public struct PathSample
    {
        public double Front, Right, Up; // canonical, normalized to stage half extents
        public double? Gain, Spread;    // null when the track has no such curve
    }

    public static class PathSampler
    {
        /// <summary>Sample a track by id at wall time (seconds). The track's speed is applied here. Throws ArgumentException for an unknown track.</summary>
        public static PathSample Sample(PathDocument document, string trackId, double time)
        {
            var track = document?.FindTrack(trackId);
            if (track == null) throw new ArgumentException("Unknown track '" + trackId + "'.");
            return Sample(track, time);
        }

        public static PathSample Sample(PathTrack track, double time)
        {
            if (track == null) throw new ArgumentNullException(nameof(track));
            if (double.IsNaN(time)) time = 0;
            time = Math.Max(-1e300, Math.Min(1e300, time)); // keep fmod finite
            var keys = track.Keyframes;
            double tau = MapTime(time, keys[0].T, keys[keys.Length - 1].T, track.Loop, track.Speed);
            var p = Position(keys, tau);
            var result = new PathSample { Front = p[0], Right = p[1], Up = p[2] };
            if (track.Gain != null) result.Gain = Curve(track.Gain, MapTime(time, track.Gain[0].T, track.Gain[track.Gain.Length - 1].T, track.Loop, track.Speed));
            if (track.Spread != null) result.Spread = Curve(track.Spread, MapTime(time, track.Spread[0].T, track.Spread[track.Spread.Length - 1].T, track.Loop, track.Speed));
            return result;
        }

        static double MapTime(double time, double first, double last, PathLoop loop, double speed)
        {
            double tau = time * speed, span = last - first;
            if (tau <= first || span == 0) return first;
            if (loop == PathLoop.Loop) return first + (tau - first) % span; // C# % on doubles is IEEE fmod, like math.fmod
            if (loop == PathLoop.PingPong)
            {
                double m = (tau - first) % (2 * span);
                return first + (m > span ? 2 * span - m : m);
            }
            return Math.Min(tau, last);
        }

        static double Ease(PathEase ease, double u)
        {
            switch (ease)
            {
                case PathEase.In: return u * u;
                case PathEase.Out: return 1 - (1 - u) * (1 - u);
                case PathEase.InOut: return u * u * (3 - 2 * u);
                default: return u;
            }
        }

        // Index i with times[i] <= tau < times[i+1] (bisect_right - 1).
        static int Segment(double tau, int count, Func<int, double> timeAt)
        {
            int lo = 0, hi = count;
            while (lo < hi) { int mid = (lo + hi) / 2; if (tau < timeAt(mid)) hi = mid; else lo = mid + 1; }
            return lo - 1;
        }

        static double[] Position(PathKeyframe[] keys, double tau)
        {
            if (tau >= keys[keys.Length - 1].T) return (double[])keys[keys.Length - 1].Position.Clone();
            int i = Segment(tau, keys.Length, n => keys[n].T);
            var a = keys[i]; var b = keys[i + 1];
            if (a.Interp == PathInterp.Hold) return (double[])a.Position.Clone();
            double u = Ease(a.Ease, (tau - a.T) / (b.T - a.T));
            var r = new double[3];
            if (a.Interp == PathInterp.Linear)
            {
                for (int k = 0; k < 3; ++k) r[k] = a.Position[k] + (b.Position[k] - a.Position[k]) * u;
            }
            else if (a.Interp == PathInterp.Bezier)
            {
                double w0 = (1 - u) * (1 - u) * (1 - u), w1 = 3 * (1 - u) * (1 - u) * u, w2 = 3 * (1 - u) * u * u, w3 = u * u * u;
                for (int k = 0; k < 3; ++k)
                    r[k] = w0 * a.Position[k] + w1 * (a.Position[k] + a.TangentOut[k]) + w2 * (b.Position[k] + b.TangentIn[k]) + w3 * b.Position[k];
            }
            else
            {
                var p0 = keys[Math.Max(i - 1, 0)].Position; var p3 = keys[Math.Min(i + 2, keys.Length - 1)].Position;
                for (int k = 0; k < 3; ++k)
                {
                    double q0 = p0[k], p1 = a.Position[k], p2 = b.Position[k], q3 = p3[k];
                    r[k] = 0.5 * (2 * p1 + (-q0 + p2) * u + (2 * q0 - 5 * p1 + 4 * p2 - q3) * u * u + (-q0 + 3 * p1 - 3 * p2 + q3) * u * u * u);
                }
            }
            return r;
        }

        static double Curve(PathCurvePoint[] points, double tau)
        {
            if (tau <= points[0].T) return points[0].Value;
            if (tau >= points[points.Length - 1].T) return points[points.Length - 1].Value;
            int i = Segment(tau, points.Length, n => points[n].T);
            var a = points[i]; var b = points[i + 1];
            return a.Value + (b.Value - a.Value) * ((tau - a.T) / (b.T - a.T));
        }
    }
}
