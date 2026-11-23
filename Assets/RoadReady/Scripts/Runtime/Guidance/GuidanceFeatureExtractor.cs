using RoadReady.Config;
using RoadReady.Core;
using RoadReady.Traffic;
using UnityEngine;

namespace RoadReady.Guidance
{
    /// <summary>
    /// Measures the guide's numbers from the live scene. It only READS the game (road zones, stop lines, other
    /// road users); it changes nothing. Its own loops are index-based, so they allocate nothing. (The existing
    /// stop-line "should stop" test it calls for zebra lines loops over the agent list the ordinary way.)
    /// </summary>
    public sealed class GuidanceFeatureExtractor
    {
        // Something is "in the car's lane" when it is ahead and within this sideways distance (lane width is about 3.5 m).
        const float k_LaneHalfWidth = 2f;
        const float k_EdgeProbeStep = 0.25f;
        // A crossing counts as "ahead" when the nearest point of it lies within about 45 degrees of the car's heading.
        const float k_AheadDot = 0.7f;

        readonly ScoringConfig m_Scoring;

        public GuidanceFeatureExtractor(ScoringConfig scoring) => m_Scoring = scoring;

        public GuidanceFeatures Extract(IRoadAgent player, float defaultSpeedLimitKmh)
        {
            var position = player.Position;
            var forward = player.Forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            var speedKmh = player.Speed * Units.MsToKmh;

            return new GuidanceFeatures
            {
                speedKmh = speedKmh,
                speedLimitKmh = SpeedLimitZone.GetLimitAt(position, defaultSpeedLimitKmh),
                stopLineDistance = StopLineDistance(player, position, forward),
                crossingDistance = CrossingDistance(position, forward),
                nearestTtc = NearestTtc(player, position, forward),
                nearestPedestrianDistance = NearestPedestrian(player, position, forward),
                edgeDistance = EdgeDistance(player, position, forward),
            };
        }

        /// <summary>Nearest stop line ahead that demands a stop right now. Uses the same "should stop" test the NPC drivers use.</summary>
        float StopLineDistance(IRoadAgent player, Vector3 position, Vector3 forward)
        {
            var front = position + forward * (player.Length * 0.5f);
            var best = GuidanceFeatures.NoneDistance;
            var lines = StopLine.All;
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (!line.AppliesTo(forward) || !line.WithinWidth(front, 1f))
                    continue;
                var distance = -line.SignedDistance(front); // positive while the line is still ahead
                if (distance <= 0f || distance >= best)
                    continue;
                if (line.NpcShouldStop(player, distance, true, true, m_Scoring.giveWayGapSeconds))
                    best = distance;
            }

            return best;
        }

        /// <summary>Distance to the nearest point of the closest crossing ahead (0 when already inside one).</summary>
        static float CrossingDistance(Vector3 position, Vector3 forward)
        {
            var best = GuidanceFeatures.NoneDistance;
            var zones = RoadZone.All;
            for (var i = 0; i < zones.Count; i++)
            {
                var zone = zones[i];
                if (!zone.IsCrossing)
                    continue;
                if (zone.Contains(position))
                    return 0f;

                var local = zone.transform.InverseTransformPoint(position);
                var half = zone.Size * 0.5f;
                local.x = Mathf.Clamp(local.x, -half.x, half.x);
                local.z = Mathf.Clamp(local.z, -half.z, half.z);
                var toNearest = zone.transform.TransformPoint(local) - position;
                toNearest.y = 0f;
                var distance = toNearest.magnitude;
                if (distance < 0.01f || distance >= best)
                    continue;
                if (Vector3.Dot(toNearest / distance, forward) >= k_AheadDot)
                    best = distance;
            }

            return best;
        }

        /// <summary>Smallest time-to-collision with a road user ahead in the car's own lane (the same maths the proximity rule uses).</summary>
        static float NearestTtc(IRoadAgent player, Vector3 position, Vector3 forward)
        {
            var right = new Vector3(forward.z, 0f, -forward.x);
            var best = GuidanceFeatures.NoneTtc;
            var agents = RoadAgentRegistry.All;
            for (var i = 0; i < agents.Count; i++)
            {
                var other = agents[i];
                if (other == player)
                    continue;
                var offset = other.Position - position;
                if (Vector3.Dot(offset, forward) <= 0f || Mathf.Abs(Vector3.Dot(offset, right)) > k_LaneHalfWidth + other.Width * 0.5f)
                    continue;
                var ttc = RoadAgentRegistry.TimeToCollision(player, other);
                if (ttc < best)
                    best = ttc;
            }

            return best;
        }

        static float NearestPedestrian(IRoadAgent player, Vector3 position, Vector3 forward)
        {
            var best = GuidanceFeatures.NoneDistance;
            var agents = RoadAgentRegistry.All;
            for (var i = 0; i < agents.Count; i++)
            {
                var other = agents[i];
                if (other == player || other.Kind != RoadUserKind.Pedestrian)
                    continue;
                var offset = other.Position - position;
                offset.y = 0f;
                var distance = offset.magnitude;
                if (distance < best && Vector3.Dot(offset, forward) > 0f)
                    best = distance;
            }

            return best;
        }

        /// <summary>
        /// Sideways distance from the car's side to the road edge, found by stepping outwards from each side until the
        /// point is no longer inside a drivable road zone (the same test the off-road rule uses). Returns NoneEdge when
        /// the car is already off the road (that is the off-road rule's job) or the scene has no road zones.
        /// </summary>
        static float EdgeDistance(IRoadAgent player, Vector3 position, Vector3 forward)
        {
            if (!RoadZone.IsOnDrivable(position))
                return GuidanceFeatures.NoneEdge;

            var right = new Vector3(forward.z, 0f, -forward.x);
            var halfWidth = player.Width * 0.5f;
            var best = GuidanceFeatures.NoneEdge;
            for (var side = -1; side <= 1; side += 2)
            {
                for (var d = 0f; d < best; d += k_EdgeProbeStep)
                {
                    if (!RoadZone.IsOnDrivable(position + right * (side * (halfWidth + d))))
                    {
                        best = d;
                        break;
                    }
                }
            }

            return best;
        }
    }
}
