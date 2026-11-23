using RoadReady.Core;
using RoadReady.Player;
using RoadReady.Traffic;
using UnityEngine;

namespace RoadReady.Rules
{
    public abstract class PedestrianRule : RoadRule
    {
        public override bool AppliesTo(Perspective perspective) => perspective == Perspective.Pedestrian;
    }

    /// <summary>
    /// Evaluates the moment the learner steps into the roadway: where (designated crossing?), the pedestrian
    /// signal, the gap accepted in traffic (pedestrian crossing gap-safety threshold, proposal 3.2.3) and whether
    /// they looked both ways first. Each is reported separately so reports can show which decision type needs
    /// practice.
    /// </summary>
    public class CrossingDecisionRule : PedestrianRule
    {
        const float k_KerbPauseSpeed = 0.9f; // m/s at the moment of stepping off: faster than a slow shuffle means no pause
        RuleContext m_Ctx;

        public override string Id => "crossing";

        public override void Begin(RuleContext ctx)
        {
            m_Ctx = ctx;
            if (ctx.pedestrian != null)
                ctx.pedestrian.CrossingStarted += OnCrossingStarted;
        }

        public override void End(RuleContext ctx)
        {
            if (ctx.pedestrian != null)
                ctx.pedestrian.CrossingStarted -= OnCrossingStarted;
            m_Ctx = null;
        }

        public override void Tick(RuleContext ctx, float dt) { }

        void OnCrossingStarted(CrossingEvent e)
        {
            var ctx = m_Ctx;
            if (ctx == null)
                return;
            var scoring = ctx.scoring;

            // 1. Location.
            if (e.crossing != null)
                ctx.Report("crossing_location", "Used a designated crossing", DecisionClass.Safe, ViolationSeverity.None);
            else if (RoadZone.FindCrossingNear(e.position, 40f) != null)
                ctx.Report("crossing_location", "Crossed away from the designated crossing", DecisionClass.Borderline, ViolationSeverity.Minor,
                    "A crossing was less than 40 m away.", "Walk to the zebra or the signalised crossing - drivers expect you there.");

            // 2. Pedestrian signal.
            if (e.crossing != null && e.crossing.Signal != null)
            {
                switch (e.crossing.PedestrianSignalState)
                {
                    case SignalState.Red:
                        ctx.Report("pedestrian_signal", "Crossed on the red pedestrian signal", DecisionClass.Unsafe, ViolationSeverity.Major,
                            null, "Wait for the green figure. Drivers with a green light will not expect you.");
                        break;
                    case SignalState.Amber:
                        ctx.Report("pedestrian_signal", "Started crossing as the signal was ending", DecisionClass.Borderline, ViolationSeverity.Minor,
                            null, "When the green figure flashes, do not start to cross - wait for the next cycle.");
                        break;
                    default:
                        ctx.Report("pedestrian_signal", "Waited for the green walk signal", DecisionClass.Safe, ViolationSeverity.None);
                        break;
                }
            }

            // 3. Gap acceptance.
            var length = Mathf.Max(3f, e.crossingLength);
            var required = length / scoring.pedestrianWalkSpeed;
            IRoadAgent threat;
            var tta = e.crossing != null
                ? CrossingGap.MinTimeToArrival(e.crossing, ctx.player, out threat)
                : CrossingGap.MinTimeToArrivalAt(e.position, e.direction, length, ctx.player, out threat);
            if (tta >= required + scoring.safeGapMarginSeconds)
                ctx.Report("gap_acceptance", "Chose a safe gap in traffic", DecisionClass.Safe, ViolationSeverity.None,
                    float.IsInfinity(tta) ? "No traffic was approaching." : $"Nearest vehicle was {tta:0.0}s away.");
            else if (tta >= required)
                ctx.Report("gap_acceptance", "Accepted a tight gap", DecisionClass.Borderline, ViolationSeverity.Minor,
                    $"{CollisionRule.Describe(threat.Kind)} was {tta:0.0}s away; crossing takes about {required:0.0}s.",
                    "Allow extra time - a vehicle can speed up, and you might trip or hesitate.");
            else
                ctx.Report("gap_acceptance", "Stepped out in front of traffic", DecisionClass.Unsafe, ViolationSeverity.Major,
                    $"{CollisionRule.Describe(threat.Kind)} was only {tta:0.0}s away; crossing takes about {required:0.0}s.",
                    "Judge the gap by time, not distance: a moto-taxi at 40 km/h covers 11 m every second.");

            // 3b. Stop at the kerb. Walking straight off the pavement gives no time to see, and no time for a driver to see you.
            // (Skipped when the game walks the learner across: that starts from standing.)
            var ped = ctx.pedestrian;
            if (ped != null && !ped.IsAutoWalking)
            {
                if (ped.Speed >= k_KerbPauseSpeed)
                    ctx.Report("kerb_pause", "Stepped off the kerb without stopping", DecisionClass.Borderline, ViolationSeverity.Minor,
                        null, "Stop at the kerb edge, look, and only then step out. A driver cannot react to someone who appears at walking pace.");
                else
                    ctx.Report("kerb_pause", "Stopped at the kerb before crossing", DecisionClass.Safe, ViolationSeverity.None);
            }

            // 4. Look both ways (Rwanda drives on the right: the near lane's traffic comes from the left).
            if (ctx.gaze != null)
            {
                ctx.gaze.LookedBothWays(e.direction, scoring.lookBothWaysYawDegrees, scoring.lookBothWaysWindowSeconds, out var left, out var right);
                if (left && right)
                    ctx.Report("look_both_ways", "Looked both ways before crossing", DecisionClass.Safe, ViolationSeverity.None);
                else
                    ctx.Report("look_both_ways", left ? "Did not look right before crossing" : right ? "Did not look left before crossing" : "Did not look before crossing",
                        DecisionClass.Borderline, ViolationSeverity.Minor, null,
                        "Look left, right, then left again. In Rwanda the first lane's traffic comes from your left.");
            }
        }
    }

    /// <summary>
    /// Walking along the road (or diagonally) instead of straight across: every extra second in the roadway is a second
    /// in the path of traffic, and drivers expect you to take the shortest way.
    /// </summary>
    public class WalkAlongRoadRule : PedestrianRule
    {
        const float k_MinSpeed = 0.6f;       // m/s: only count real walking
        const float k_MaxAlignment = 0.6f;   // |cos| of the angle to the crossing direction below which the walk is "sideways"
        const float k_SecondsSideways = 2.5f;

        float m_Sideways;
        bool m_Reported;

        public override string Id => "walk_along_road";

        public override void Begin(RuleContext ctx)
        {
            m_Sideways = 0f;
            m_Reported = false;
        }

        public override void Tick(RuleContext ctx, float dt)
        {
            var ped = ctx.pedestrian;
            if (ped == null || !ped.IsOnRoadway)
            {
                m_Sideways = 0f;
                m_Reported = false;
                return;
            }

            var velocity = ped.Velocity;
            velocity.y = 0f;
            if (velocity.magnitude < k_MinSpeed)
                return;
            var alignment = Mathf.Abs(Vector3.Dot(velocity.normalized, ped.CurrentCrossing.direction));
            m_Sideways = alignment < k_MaxAlignment ? m_Sideways + dt : Mathf.Max(0f, m_Sideways - dt);
            if (!m_Reported && m_Sideways >= k_SecondsSideways)
            {
                m_Reported = true;
                ctx.Report(Id, "Walked along the road instead of straight across", DecisionClass.Borderline, ViolationSeverity.Minor,
                    null, "Cross by the shortest way, straight to the other kerb. Walking along the road keeps you in the path of traffic for longer.");
            }
        }
    }

    /// <summary>Standing in the roadway far longer than the crossing takes.</summary>
    public class RoadwayDwellRule : PedestrianRule
    {
        bool m_Reported;

        public override string Id => "roadway_dwell";

        public override void Begin(RuleContext ctx) => m_Reported = false;

        public override void Tick(RuleContext ctx, float dt)
        {
            var ped = ctx.pedestrian;
            if (ped == null || !ped.IsOnRoadway)
            {
                m_Reported = false;
                return;
            }

            var expected = Mathf.Max(3f, ped.CurrentCrossing.crossingLength) / ctx.scoring.pedestrianWalkSpeed;
            if (!m_Reported && ctx.Now - ped.RoadwayEnterTime > expected + ctx.scoring.roadwayDwellGraceSeconds)
            {
                m_Reported = true;
                ctx.Report(Id, "Lingered in the roadway", DecisionClass.Borderline, ViolationSeverity.Minor,
                    null, "Once you start crossing, keep walking at a steady pace until you reach the kerb.");
            }
        }
    }
}
