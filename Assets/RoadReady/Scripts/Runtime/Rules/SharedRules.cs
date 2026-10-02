using System.Collections.Generic;
using RoadReady.Core;
using RoadReady.Traffic;
using UnityEngine;

namespace RoadReady.Rules
{
    /// <summary>Any overlap between the learner and another road user or roadside object (Critical).</summary>
    public class CollisionRule : RoadRule
    {
        readonly HashSet<IRoadAgent> m_Hit = new HashSet<IRoadAgent>();
        RuleContext m_Ctx;

        public override string Id => "collision";

        public override void Begin(RuleContext ctx)
        {
            m_Ctx = ctx;
            m_Hit.Clear();
            if (ctx.driver != null)
                ctx.driver.ObstacleHit += OnObstacleHit;
        }

        public override void End(RuleContext ctx)
        {
            if (ctx.driver != null)
                ctx.driver.ObstacleHit -= OnObstacleHit;
            m_Ctx = null;
        }

        void OnObstacleHit(Collider obstacle)
        {
            if (m_Ctx == null)
                return;
            m_Ctx.Report(Id, "Hit a roadside object", DecisionClass.Unsafe, ViolationSeverity.Critical,
                $"Collided with {obstacle.name}.", "Keep your speed down and your eyes on where you want the car to go.");
            m_Ctx.onCollision?.Invoke(null, obstacle.name);
        }

        public override void Tick(RuleContext ctx, float dt)
        {
            var player = ctx.player;
            foreach (var other in RoadAgentRegistry.All)
            {
                if (other == player || m_Hit.Contains(other))
                    continue;
                if (!RoadAgentRegistry.Overlaps(player, other, 0.05f))
                    continue;
                m_Hit.Add(other);

                var what = Describe(other.Kind);
                var tip = ctx.perspective == Perspective.Driver
                    ? "Scan ahead and to the sides, slow down near crossings and junctions, and cover the brake."
                    : "Only step out when you are sure every approaching driver has seen you and is stopping.";
                ctx.Report(Id, ctx.perspective == Perspective.Driver ? $"Collided with {what}" : $"Hit by {what}",
                    DecisionClass.Unsafe, ViolationSeverity.Critical, $"Impact at {ctx.player.Speed * Units.MsToKmh:0} km/h.", tip);

                if (other is PedestrianAI pedestrian)
                    pedestrian.OnHitByVehicle();
                ctx.onCollision?.Invoke(other, what);
            }
        }

        public static string Describe(RoadUserKind kind) => kind switch
        {
            RoadUserKind.MotoTaxi => "a moto-taxi",
            RoadUserKind.Bus => "a bus",
            RoadUserKind.Truck => "a truck",
            RoadUserKind.Bicycle => "a cyclist",
            RoadUserKind.Pedestrian => "a pedestrian",
            _ => "a car",
        };
    }

    /// <summary>
    /// Near misses and close calls by time-to-collision (collision-proximity thresholds, proposal 3.2.3).
    /// Hazard actors are skipped because their proximity is already part of the hazard evaluation.
    /// </summary>
    public class ProximityRule : RoadRule
    {
        const float k_Cooldown = 6f;

        readonly Dictionary<IRoadAgent, float> m_LastReport = new Dictionary<IRoadAgent, float>();
        readonly Dictionary<IRoadAgent, float> m_MinTtc = new Dictionary<IRoadAgent, float>();

        public override string Id => "proximity";

        public override void Begin(RuleContext ctx)
        {
            m_LastReport.Clear();
            m_MinTtc.Clear();
        }

        public override void Tick(RuleContext ctx, float dt)
        {
            var player = ctx.player;
            if (ctx.perspective == Perspective.Pedestrian && ctx.pedestrian != null && !ctx.pedestrian.IsOnRoadway)
                return;

            foreach (var other in RoadAgentRegistry.All)
            {
                if (other == player || (ctx.isHazardActor != null && ctx.isHazardActor(other)))
                    continue;
                if (ctx.perspective == Perspective.Pedestrian && !other.Kind.IsVehicle())
                    continue;
                if (m_LastReport.TryGetValue(other, out var last) && ctx.Now - last < k_Cooldown)
                    continue;

                var ttc = RoadAgentRegistry.TimeToCollision(player, other);
                if (float.IsInfinity(ttc))
                {
                    if (m_MinTtc.TryGetValue(other, out var min))
                    {
                        // Encounter over: report the worst moment once.
                        m_MinTtc.Remove(other);
                        ReportEncounter(ctx, other, min);
                    }

                    continue;
                }

                var closing = (other.Velocity - player.Velocity).magnitude;
                if (ttc < ctx.scoring.warningTimeToCollision && closing > 3f)
                {
                    m_MinTtc[other] = m_MinTtc.TryGetValue(other, out var current) ? Mathf.Min(current, ttc) : ttc;
                }
            }
        }

        void ReportEncounter(RuleContext ctx, IRoadAgent other, float minTtc)
        {
            m_LastReport[other] = ctx.Now;
            var what = CollisionRule.Describe(other.Kind);
            if (minTtc < ctx.scoring.criticalTimeToCollision)
            {
                ctx.Report(Id, $"Near miss with {what}", DecisionClass.Unsafe, ViolationSeverity.Major,
                    $"Only {minTtc:0.0}s from impact.", ctx.perspective == Perspective.Driver
                        ? "Leave more space and slow down earlier - you were less than a second from a crash."
                        : "A driver had less than a second to avoid you. Wait for a bigger gap.");
            }
            else
            {
                ctx.Report(Id, $"Close call with {what}", DecisionClass.Borderline, ViolationSeverity.Minor,
                    $"{minTtc:0.0}s from impact.", "Anticipate earlier so you are never within two seconds of a collision.");
            }
        }

        public override void End(RuleContext ctx)
        {
            foreach (var pair in m_MinTtc)
                ReportEncounter(ctx, pair.Key, pair.Value);
            m_MinTtc.Clear();
        }
    }
}
