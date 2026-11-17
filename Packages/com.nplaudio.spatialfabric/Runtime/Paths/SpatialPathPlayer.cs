// Copyright (c) 2026 SpatialFabric Contributors. MIT.
using UnityEngine;
using NPLAudio.SpatialFabric.Paths;
namespace NPLAudio.SpatialFabric
{
    /// <summary>
    /// Plays a baked Path Studio path by moving a Transform deterministically. Position only; no audio, OSC or
    /// network. Nothing moves until Play()/Seek()/Evaluate() is called (or playOnEnable is set).
    /// Canonical [front,right,up] positions are mapped through the stage's dimensions/flips and transform.
    /// </summary>
    [DisallowMultipleComponent, AddComponentMenu("SpatialFabric/Spatial Path Player")]
    public sealed class SpatialPathPlayer : MonoBehaviour
    {
        public SpatialPathAsset asset;
        [Tooltip("Track id inside the asset. Empty = first track.")] public string trackId = "";
        [Tooltip("Stage whose transform/dimensions define the path space. Empty = first SpatialStage in this scene.")] public SpatialStage stage;
        [Tooltip("Transform to move. Empty = this object (or its SpatialObject pose).")] public Transform target;
        public bool playOnEnable;
        public bool unscaledTime;
        [Min(0)] public float playbackSpeed = 1;

        double time;
        bool playing;
        string lastError = "", warnedError;
        double? gain, spread;

        public double Time => time;
        public bool IsPlaying => playing;
        /// <summary>Empty when the last Evaluate succeeded; otherwise why nothing moved.</summary>
        public string LastError => lastError;
        /// <summary>Optional path parameters at the last evaluated time; exposed only, not applied to anything.</summary>
        public double? Gain => gain;
        public double? Spread => spread;

        /// <summary>The selected track (trackId, or the first track) or null when unavailable.</summary>
        public PathTrack ResolvedTrack
        {
            get
            {
                var doc = asset != null ? asset.Document : null;
                if (doc == null) return null;
                return string.IsNullOrEmpty(trackId) ? doc.Tracks[0] : doc.FindTrack(trackId);
            }
        }
        /// <summary>Wall-clock seconds to reach the last keyframe once (0 when no track).</summary>
        public double Duration { get { var t = ResolvedTrack; return t != null ? t.Duration : 0; } }

        public Transform MovedTransform
        {
            get
            {
                if (target != null) return target;
                var so = GetComponent<SpatialObject>();
                return so != null ? so.Pose : transform;
            }
        }

        void OnEnable()
        {
            ResolveStage();
            if (playOnEnable) Play();
        }

        void OnDisable() { playing = false; }

        SpatialStage ResolveStage()
        {
            if (stage != null) return stage;
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                stage = root.GetComponentInChildren<SpatialStage>(true);
                if (stage != null) break;
            }
            return stage;
        }

        public void Play() { playing = true; Evaluate(time); }
        public void Pause() { playing = false; }
        public void Stop() { playing = false; time = 0; Evaluate(0); }
        public void Seek(double seconds) { time = SanitizeTime(seconds); Evaluate(time); }

        static double SanitizeTime(double seconds) => double.IsNaN(seconds) || seconds < 0 ? 0 : seconds;

        void Update()
        {
            if (!playing) return;
            double dt = unscaledTime ? UnityEngine.Time.unscaledDeltaTime : UnityEngine.Time.deltaTime;
            time += dt * Mathf.Max(0, playbackSpeed);
            Evaluate(time);
        }

        /// <summary>Move the transform to the path position at <paramref name="seconds"/> without changing Time or play state.</summary>
        public void Evaluate(double seconds)
        {
            string error = null;
            var track = ResolvedTrack;
            var st = ResolveStage();
            if (asset == null) error = "No path asset assigned.";
            else if (!asset.IsValid) error = "Path asset is invalid: " + asset.ProblemSummary();
            else if (track == null) error = "Track '" + trackId + "' not found in path asset.";
            else if (st == null) error = "No SpatialStage assigned or found in this scene.";
            if (error == null)
            {
                var s = PathSampler.Sample(track, SanitizeTime(seconds));
                var d = st.dimensionsMeters; // x = front, y = right, z = up
                var local = PathStageMath.ToLocal(new PathStageFrame(d.x, d.y, d.z, st.flipFront, st.flipRight, st.flipUp), s.Front, s.Right, s.Up);
                gain = s.Gain; spread = s.Spread;
                MovedTransform.position = st.transform.TransformPoint(new Vector3((float)local.x, (float)local.y, (float)local.z));
                lastError = "";
                warnedError = null;
                return;
            }
            lastError = error; gain = null; spread = null;
            if (warnedError != error) { warnedError = error; Debug.LogWarning("SpatialPathPlayer '" + name + "': " + error, this); }
        }
    }
}
