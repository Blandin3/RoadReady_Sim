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
