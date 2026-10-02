using RoadReady.Core;
using UnityEngine;

namespace RoadReady.Traffic
{
    /// <summary>
    /// Gap-acceptance maths: how many seconds until the next non-yielding vehicle reaches a crossing line.
    /// Used by NPC pedestrians to decide when to cross, and by the pedestrian gap rule to judge the learner.
    /// </summary>
    public static class CrossingGap
    {
        /// <summary>
        /// Smallest time-to-arrival of any approaching vehicle at the segment <paramref name="a"/>-<paramref name="b"/>.
        /// </summary>
        public static float MinTimeToArrival(Vector3 a, Vector3 b, IRoadAgent exclude, out IRoadAgent threat, float segmentMargin = 3f)
        {
            threat = null;
            var best = float.PositiveInfinity;
            var ab = b - a;
            ab.y = 0f;
            var length = ab.magnitude;
            if (length < 0.1f)
                return best;
            var dir = ab / length;
            var normal = new Vector3(dir.z, 0f, -dir.x);

            foreach (var agent in RoadAgentRegistry.All)
            {
                if (agent == exclude || !agent.Kind.IsVehicle())
                    continue;
                if (agent.Speed < 0.5f || (agent.IsYielding && agent.Speed < 3f))
                    continue;

                var p = agent.Position;
                var d0 = Vector3.Dot(p - a, normal);
                var dv = Vector3.Dot(agent.Velocity, normal);

                float t;
                if (Mathf.Abs(d0) <= agent.Length * 0.5f)
                {
                    t = 0f; // occupying the crossing line right now
                }
                else
                {
                    if (Mathf.Abs(dv) < 0.1f || Mathf.Sign(dv) == Mathf.Sign(d0))
                        continue; // parallel or moving away
                    t = (Mathf.Abs(d0) - agent.Length * 0.5f) / Mathf.Abs(dv);
                }

                var q = p + agent.Velocity * t;
                var along = Vector3.Dot(q - a, dir);
                if (along < -segmentMargin || along > length + segmentMargin)
                    continue;

                if (t < best)
                {
                    best = t;
                    threat = agent;
                }
            }

            return best;
        }

        public static float MinTimeToArrival(RoadZone crossing, IRoadAgent exclude, out IRoadAgent threat)
        {
            threat = null;
            if (crossing == null || crossing.KerbA == null || crossing.KerbB == null)
                return float.PositiveInfinity;
            return MinTimeToArrival(crossing.KerbA.position, crossing.KerbB.position, exclude, out threat);
        }

        /// <summary>Approximates a crossing line through <paramref name="position"/> perpendicular to the nearest carriageway.</summary>
        public static float MinTimeToArrivalAt(Vector3 position, Vector3 crossingDirection, float crossingLength, IRoadAgent exclude, out IRoadAgent threat)
        {
            crossingDirection.y = 0f;
            crossingDirection.Normalize();
            return MinTimeToArrival(position, position + crossingDirection * crossingLength, exclude, out threat);
        }
    }
}
