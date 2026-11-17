// Copyright (c) 2026 SpatialFabric Contributors. MIT.
// Pure C# (System only). Canonical [front, right, up] (normalized to stage half extents) to stage-local
// Unity axes (x = right, y = up, z = front). Exact inverse of ContextExportCore.ToCanonical.
using System;

namespace NPLAudio.SpatialFabric.Paths
{
    /// <summary>Stage dimensions (full size, canonical front/right/up meters) and axis flips; mirrors SpatialStage.</summary>
    public struct PathStageFrame
    {
        public double DimFront, DimRight, DimUp;
        public bool FlipFront, FlipRight, FlipUp;
        public PathStageFrame(double front, double right, double up, bool flipFront = false, bool flipRight = false, bool flipUp = false)
        {
            DimFront = front; DimRight = right; DimUp = up; FlipFront = flipFront; FlipRight = flipRight; FlipUp = flipUp;
        }
    }

    public static class PathStageMath
    {
        public const double MinHalfExtentMeters = .05;

        static double Half(double dim) => Math.Max(MinHalfExtentMeters, dim * .5);

        /// <summary>Canonical (front,right,up) to stage-local Unity (x,y,z) meters.</summary>
        public static (double x, double y, double z) ToLocal(PathStageFrame f, double front, double right, double up)
        {
            return (right * Half(f.DimRight) * (f.FlipRight ? -1 : 1),
                up * Half(f.DimUp) * (f.FlipUp ? -1 : 1),
                front * Half(f.DimFront) * (f.FlipFront ? -1 : 1));
        }
    }
}
