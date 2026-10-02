using System.Collections.Generic;
using RoadReady.Core;
using UnityEngine;

namespace RoadReady.Traffic
{
    /// <summary>Anything that occupies road space: NPC vehicles, NPC pedestrians, the player car and the player on foot.</summary>
    public interface IRoadAgent
    {
        RoadUserKind Kind { get; }
        Vector3 Position { get; }
        Vector3 Velocity { get; }
        Vector3 Forward { get; }
        float Speed { get; }
        /// <summary>Footprint length (along forward) in metres.</summary>
        float Length { get; }
        /// <summary>Footprint width in metres.</summary>
        float Width { get; }
        bool IsPlayer { get; }
        /// <summary>True while deliberately slowing / stopped for another road user (excluded from gap threats).</summary>
        bool IsYielding { get; }
        Transform Transform { get; }
    }

    /// <summary>Central registry used for time-to-collision, gap and overlap queries (no physics dependency).</summary>
    public static class RoadAgentRegistry
    {
        static readonly List<IRoadAgent> s_Agents = new List<IRoadAgent>();

        public static IReadOnlyList<IRoadAgent> All => s_Agents;

        public static IRoadAgent Player { get; private set; }

        // Keeps the registry clean when domain reload is disabled in Enter Play Mode settings.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Clear();

        public static void Register(IRoadAgent agent)
        {
            if (!s_Agents.Contains(agent))
                s_Agents.Add(agent);
            if (agent.IsPlayer)
                Player = agent;
        }

        public static void Unregister(IRoadAgent agent)
        {
            s_Agents.Remove(agent);
            if (Player == agent)
                Player = null;
        }

        public static void Clear()
        {
            s_Agents.Clear();
            Player = null;
        }

        /// <summary>
        /// Conflict radius for circle-based TTC. Deliberately tighter than the bounding circle so traffic in the
        /// adjacent lane (3.5 m apart) is not treated as a collision course.
        /// </summary>
        public static float Radius(IRoadAgent agent) => 0.5f * (agent.Width + 0.3f * agent.Length);

        /// <summary>Oriented-rectangle overlap test on the ground plane (separating axis theorem).</summary>
        public static bool Overlaps(IRoadAgent a, IRoadAgent b, float margin = 0f)
        {
            var pa = Kinematics.Flat(a.Position);
            var pb = Kinematics.Flat(b.Position);
            var fa = Kinematics.Flat(a.Forward).normalized;
            var fb = Kinematics.Flat(b.Forward).normalized;
            if (fa.sqrMagnitude < 0.5f) fa = Vector2.up;
            if (fb.sqrMagnitude < 0.5f) fb = Vector2.up;
            var ra = new Vector2(fa.y, -fa.x);
            var rb = new Vector2(fb.y, -fb.x);
            var ha = new Vector2(a.Width * 0.5f + margin, a.Length * 0.5f + margin);
            var hb = new Vector2(b.Width * 0.5f + margin, b.Length * 0.5f + margin);
            var d = pb - pa;

            foreach (var axis in new[] { fa, ra, fb, rb })
            {
                var projA = ha.x * Mathf.Abs(Vector2.Dot(ra, axis)) + ha.y * Mathf.Abs(Vector2.Dot(fa, axis));
                var projB = hb.x * Mathf.Abs(Vector2.Dot(rb, axis)) + hb.y * Mathf.Abs(Vector2.Dot(fb, axis));
                if (Mathf.Abs(Vector2.Dot(d, axis)) > projA + projB)
                    return false;
            }

            return true;
        }

        /// <summary>Time-to-collision between two agents using bounding circles, infinity if not on a collision course.</summary>
        public static float TimeToCollision(IRoadAgent a, IRoadAgent b) =>
            Kinematics.TimeToCollision(a.Position, a.Velocity, Radius(a), b.Position, b.Velocity, Radius(b));

        /// <summary>
        /// Finds the nearest agent ahead of <paramref name="self"/> inside a lane-width corridor.
        /// Used for car-following (NPCs) and the following-distance rule (player).
        /// </summary>
        public static IRoadAgent FindLeader(IRoadAgent self, Vector3 forward, float lookAhead, float corridorHalfWidth, out float gap, bool includePedestrians = true)
        {
            IRoadAgent best = null;
            gap = float.PositiveInfinity;
            var origin = self.Position;
            forward.y = 0f;
            forward.Normalize();
            var right = new Vector3(forward.z, 0f, -forward.x);

            foreach (var other in s_Agents)
            {
                if (other == self)
                    continue;
                if (!includePedestrians && other.Kind == RoadUserKind.Pedestrian)
                    continue;
                var offset = other.Position - origin;
                var along = Vector3.Dot(offset, forward);
                if (along <= 0f || along > lookAhead)
                    continue;
                var lateral = Mathf.Abs(Vector3.Dot(offset, right));
                if (lateral > corridorHalfWidth + other.Width * 0.5f)
                    continue;
                var g = along - self.Length * 0.5f - other.Length * 0.5f;
                if (g < gap)
                {
                    gap = Mathf.Max(0f, g);
                    best = other;
                }
            }

            return best;
        }
    }
}
