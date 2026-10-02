using System;
using UnityEngine;

namespace RoadReady.Core
{
    /// <summary>
    /// Seeded random source used by traffic and compliance rolls so every attempt of the same
    /// scenario plays out identically - this is what makes pre/post comparisons fair and lets the
    /// same learner meet the "identical" situation from both perspectives.
    /// </summary>
    public static class RoadReadyRandom
    {
        static System.Random s_Random = new System.Random(12345);

        public static int Seed { get; private set; } = 12345;

        public static void Init(int seed)
        {
            Seed = seed;
            s_Random = new System.Random(seed);
        }

        public static float Value => (float)s_Random.NextDouble();

        public static float Range(float min, float max) => min + (max - min) * Value;

        public static int Range(int minInclusive, int maxExclusive) => s_Random.Next(minInclusive, maxExclusive);

        public static bool Chance(float probability) => Value < probability;
    }

    /// <summary>Attempt-relative clock. All telemetry timestamps are seconds since attempt start.</summary>
    public static class SimClock
    {
        static float s_StartTime;

        public static bool Running { get; private set; }

        public static float Now => Running ? Time.time - s_StartTime : 0f;

        public static void Begin()
        {
            s_StartTime = Time.time;
            Running = true;
        }

        public static void Stop()
        {
            Running = false;
        }
    }

    /// <summary>Loose coupling between modules (NFR maintainability): publishers never reference subscribers.</summary>
    public static class RoadReadyEvents
    {
        public static event Action<AppState, AppState> AppStateChanged;
        public static event Action<string> InstructionRequested;
        public static event Action<string, float> ToastRequested;
        public static event Action<bool> PauseRequested;
        public static event Action HazardSpottedPressed;

        public static void RaiseAppStateChanged(AppState from, AppState to) => AppStateChanged?.Invoke(from, to);
        public static void RequestInstruction(string key) => InstructionRequested?.Invoke(key);
        public static void Toast(string message, float seconds = 2.5f) => ToastRequested?.Invoke(message, seconds);
        public static void RequestPause(bool paused) => PauseRequested?.Invoke(paused);
        public static void RaiseHazardSpotted() => HazardSpottedPressed?.Invoke();
    }

    public static class Units
    {
        public const float MsToKmh = 3.6f;
        public const float KmhToMs = 1f / 3.6f;
    }

    /// <summary>Planar (XZ) kinematics helpers used for time-to-collision and gap computations.</summary>
    public static class Kinematics
    {
        public static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        /// <summary>
        /// Returns the time until two constant-velocity bodies are closest, and the distance at that moment.
        /// Returns false if they are already separating.
        /// </summary>
        public static bool ClosestApproach(Vector3 posA, Vector3 velA, Vector3 posB, Vector3 velB, out float time, out float distance)
        {
            var p = Flat(posB - posA);
            var v = Flat(velB - velA);
            var vv = v.sqrMagnitude;
            if (vv < 1e-4f)
            {
                time = float.PositiveInfinity;
                distance = p.magnitude;
                return false;
            }

            time = -Vector2.Dot(p, v) / vv;
            if (time < 0f)
            {
                distance = p.magnitude;
                return false;
            }

            distance = (p + v * time).magnitude;
            return true;
        }

        /// <summary>
        /// Time-to-collision estimate: time of closest approach when the closest distance is inside the
        /// combined radius, otherwise infinity.
        /// </summary>
        public static float TimeToCollision(Vector3 posA, Vector3 velA, float radiusA, Vector3 posB, Vector3 velB, float radiusB)
        {
            if (!ClosestApproach(posA, velA, posB, velB, out var t, out var d))
                return float.PositiveInfinity;
            return d <= radiusA + radiusB ? t : float.PositiveInfinity;
        }

        /// <summary>Signed yaw from <paramref name="from"/> to <paramref name="to"/> in degrees (positive = right).</summary>
        public static float SignedYaw(Vector3 from, Vector3 to)
        {
            from.y = 0f;
            to.y = 0f;
            return Vector3.SignedAngle(from, to, Vector3.up);
        }
    }
}
