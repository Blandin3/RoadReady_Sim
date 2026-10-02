using System.Collections.Generic;
using RoadReady.Core;
using RoadReady.Traffic;
using UnityEngine;

namespace RoadReady.Rules
{
    public abstract class DriverRule : RoadRule
    {
        public override bool AppliesTo(Perspective perspective) => perspective == Perspective.Driver;
    }

    /// <summary>Speed-limit compliance with a grace period; one decision per excursion.</summary>
    public class SpeedLimitRule : DriverRule
    {
        float m_OverTime;
        float m_MaxRatio;
        float m_ExcursionLimit;
        bool m_InExcursion;
        bool m_AnyViolation;
        float m_DrivenTime;

        public override string Id => "speed_limit";

        public override void Begin(RuleContext ctx)
        {
            m_OverTime = 0f;
            m_MaxRatio = 0f;
            m_InExcursion = false;
            m_AnyViolation = false;
            m_DrivenTime = 0f;
        }

        public override void Tick(RuleContext ctx, float dt)
        {
            var speed = ctx.driver.SpeedKmh;
            if (speed > 5f)
                m_DrivenTime += dt;
            var limit = SpeedLimitZone.GetLimitAt(ctx.driver.Position, ctx.parameters.speedLimitKmh);
            var over = speed > limit + ctx.scoring.speedToleranceKmh;

            if (over)
            {
                m_OverTime += dt;
                m_MaxRatio = Mathf.Max(m_MaxRatio, speed / limit);
                m_ExcursionLimit = limit;
                if (m_OverTime >= ctx.scoring.speedingGraceSeconds)
                    m_InExcursion = true;
            }
            else
            {
                if (m_InExcursion)
                    Close(ctx);
                m_OverTime = 0f;
                m_MaxRatio = 0f;
            }
        }

        void Close(RuleContext ctx)
        {
            m_InExcursion = false;
            m_AnyViolation = true;
            var peak = m_MaxRatio * m_ExcursionLimit;
            var unsafeSpeed = m_MaxRatio >= ctx.scoring.unsafeSpeedingRatio;
            ctx.Report(Id, $"Exceeded the {m_ExcursionLimit:0} km/h limit", unsafeSpeed ? DecisionClass.Unsafe : DecisionClass.Borderline,
                unsafeSpeed ? ViolationSeverity.Major : ViolationSeverity.Minor, $"Peak {peak:0} km/h for {m_OverTime:0.0}s.",
                "In busy Kigali streets pedestrians and moto-taxis appear suddenly - lower speed gives you time to react.");
        }

        public override void End(RuleContext ctx)
        {
            if (m_InExcursion)
                Close(ctx);
            if (!m_AnyViolation && m_DrivenTime > 10f)
                ctx.Report(Id, "Kept to the speed limit", DecisionClass.Safe, ViolationSeverity.None);
        }
    }

    /// <summary>
    /// Evaluates every crossing of a stop line by the car's front bumper: red / amber signals, yielding at
    /// zebra crossings and giving way at unsignalised junctions.
    /// </summary>
    public class StopLineRule : DriverRule
    {
        class LineState
        {
            public float previousDistance = float.NaN;
            public bool stoppedForDemand;
            public float amberDistance = float.NaN;
            public float amberSpeed;
            public SignalState lastSignal = SignalState.Off;
        }

        readonly Dictionary<StopLine, LineState> m_States = new Dictionary<StopLine, LineState>();

        public override string Id => "stop_line";

        public override void Begin(RuleContext ctx) => m_States.Clear();

        public override void Tick(RuleContext ctx, float dt)
        {
            var driver = ctx.driver;
            var front = driver.Position + driver.Forward * (driver.Length * 0.5f);

            foreach (var line in StopLine.All)
            {
                if (!line.AppliesTo(driver.Forward) || !line.WithinWidth(front, 1f))
                    continue;
                if (!m_States.TryGetValue(line, out var state))
                    m_States[line] = state = new LineState();

                var d = line.SignedDistance(front);
                if (d < 0f && d > -30f)
                    TrackApproach(ctx, line, state, d);

                if (!float.IsNaN(state.previousDistance) && state.previousDistance < 0f && d >= 0f)
                    Evaluate(ctx, line, state);
                state.previousDistance = d;
            }
        }

        void TrackApproach(RuleContext ctx, StopLine line, LineState state, float d)
        {
            var stopped = ctx.driver.SpeedKmh < 2f && d > -15f;
            switch (line.Type)
            {
                case StopLineType.Signal:
                    var signal = line.SignalStateNow;
                    if (signal == SignalState.Amber && state.lastSignal != SignalState.Amber)
                    {
                        state.amberDistance = -d;
                        state.amberSpeed = ctx.driver.Speed;
                    }

                    if (signal == SignalState.Red && stopped)
                        state.stoppedForDemand = true;
                    state.lastSignal = signal;
                    break;
                case StopLineType.ZebraYield:
                    if (stopped && line.HasPedestrianDemand(ctx.player))
                        state.stoppedForDemand = true;
                    break;
                case StopLineType.GiveWay:
                    if (stopped)
                        state.stoppedForDemand = true;
                    break;
            }
        }

        void Evaluate(RuleContext ctx, StopLine line, LineState state)
        {
            switch (line.Type)
            {
                case StopLineType.Signal:
                    var signal = line.SignalStateNow;
                    if (signal == SignalState.Red)
                    {
                        ctx.Report("red_light", "Drove through a red light", DecisionClass.Unsafe, ViolationSeverity.Major,
                            "The signal was red when you crossed the stop line.", "Red means stop behind the line - every time, even if the junction looks empty.");
                    }
                    else if (signal == SignalState.Amber && !float.IsNaN(state.amberDistance) &&
                             state.amberDistance > state.amberSpeed * state.amberSpeed / (2f * 4f) + 2f)
                    {
                        ctx.Report("red_light", "Entered on amber when you could have stopped", DecisionClass.Borderline, ViolationSeverity.Minor,
                            $"You were {state.amberDistance:0} m away when it turned amber.", "Amber means stop unless you are too close to stop safely.");
                    }
                    else if (state.stoppedForDemand)
                    {
                        ctx.Report("red_light", "Waited for the green light", DecisionClass.Safe, ViolationSeverity.None);
                    }

                    break;

                case StopLineType.ZebraYield:
                    if (line.HasPedestrianDemand(ctx.player))
                    {
                        ctx.Report("zebra_yield", "Did not give way at the zebra crossing", DecisionClass.Unsafe, ViolationSeverity.Major,
                            "A pedestrian was on or waiting at the crossing.", "At a zebra crossing, pedestrians have priority. Slow down early and stop for anyone waiting.");
                    }
                    else if (state.stoppedForDemand)
                    {
                        ctx.Report("zebra_yield", "Stopped for pedestrians at the zebra", DecisionClass.Safe, ViolationSeverity.None);
                    }

                    break;

                case StopLineType.GiveWay:
                    var tta = line.PriorityTrafficTimeToArrival(ctx.player, out var threat);
                    if (tta < ctx.scoring.giveWayGapSeconds)
                    {
                        ctx.Report("give_way", "Pulled out in front of priority traffic", DecisionClass.Unsafe, ViolationSeverity.Major,
                            $"{CollisionRule.Describe(threat.Kind)} was {tta:0.0}s from the junction.",
                            "Wait for a gap of at least three seconds. Moto-taxis are small and fast - look twice.");
                    }
                    else if (ctx.driver.SpeedKmh > ctx.scoring.giveWayMaxEntrySpeedKmh && !state.stoppedForDemand)
                    {
                        ctx.Report("give_way", "Entered the junction too fast", DecisionClass.Borderline, ViolationSeverity.Minor,
                            $"{ctx.driver.SpeedKmh:0} km/h at the give-way line.", "Approach give-way lines slowly enough to stop if something appears.");
                    }
                    else
                    {
                        ctx.Report("give_way", "Gave way correctly at the junction", DecisionClass.Safe, ViolationSeverity.None);
                    }

                    break;
            }

            state.stoppedForDemand = false;
            state.amberDistance = float.NaN;
        }
    }

    /// <summary>Time headway to the vehicle ahead (moto-taxis stop suddenly at informal pickup points).</summary>
    public class FollowingDistanceRule : DriverRule
    {
        float m_CloseTime;
        float m_MinHeadway;
        bool m_Reported;

        public override string Id => "following_distance";

        public override void Begin(RuleContext ctx)
        {
            m_CloseTime = 0f;
            m_MinHeadway = float.PositiveInfinity;
            m_Reported = false;
        }

        public override void Tick(RuleContext ctx, float dt)
        {
            var driver = ctx.driver;
            if (driver.SpeedKmh < 15f)
            {
                Reset();
                return;
            }

            var leader = RoadAgentRegistry.FindLeader(driver, driver.Forward, 60f, 1.2f, out var gap, includePedestrians: false);
            if (leader == null || Vector3.Dot(leader.Forward, driver.Forward) < 0.7f)
            {
                Reset();
                return;
            }

            var headway = gap / Mathf.Max(0.1f, driver.Speed);
            if (headway < ctx.scoring.minFollowingHeadwaySeconds)
            {
                m_CloseTime += dt;
                m_MinHeadway = Mathf.Min(m_MinHeadway, headway);
                if (m_CloseTime >= ctx.scoring.followingGraceSeconds && !m_Reported)
                {
                    m_Reported = true;
                    ctx.Report(Id, $"Followed {CollisionRule.Describe(leader.Kind)} too closely", DecisionClass.Borderline, ViolationSeverity.Minor,
                        $"Gap was only {m_MinHeadway:0.0}s.", "Keep at least a two-second gap - count 'one thousand and one, one thousand and two'.");
                }
            }
            else
            {
                Reset();
            }
        }

        void Reset()
        {
            m_CloseTime = 0f;
            m_MinHeadway = float.PositiveInfinity;
            m_Reported = false;
        }
    }

    /// <summary>Turning inside a junction zone without signalling the matching side.</summary>
    public class TurnSignalRule : DriverRule
    {
        const float k_SignalLookback = 5f;

        RoadZone m_Junction;
        float m_EntryHeading;
        bool m_SignalledLeft;
        bool m_SignalledRight;

        public override string Id => "turn_signal";

        public override void Begin(RuleContext ctx) => m_Junction = null;

        public override void Tick(RuleContext ctx, float dt)
        {
            var driver = ctx.driver;
            var zone = RoadZone.FindFirst(driver.Position, z => z.Type == RoadZoneType.Junction);

            if (zone != null && m_Junction == null)
            {
                m_Junction = zone;
                m_EntryHeading = driver.transform.eulerAngles.y;
                var recent = Time.time - driver.LastIndicatorTime <= k_SignalLookback;
                m_SignalledLeft = driver.Indicator == TurnSide.Left;
                m_SignalledRight = driver.Indicator == TurnSide.Right;
                if (recent && driver.Indicator == TurnSide.None)
                {
                    // Indicator already auto-cancelled; treat recent use as a signal of unknown side.
                    m_SignalledLeft = m_SignalledRight = true;
                }
            }

            if (m_Junction == null)
                return;

            m_SignalledLeft |= driver.Indicator == TurnSide.Left;
            m_SignalledRight |= driver.Indicator == TurnSide.Right;

            if (zone == null)
            {
                var delta = Mathf.DeltaAngle(m_EntryHeading, driver.transform.eulerAngles.y);
                m_Junction = null;
                if (Mathf.Abs(delta) < ctx.scoring.turnDetectionDegrees)
                    return;
                var right = delta > 0f;
                var signalled = right ? m_SignalledRight : m_SignalledLeft;
                if (signalled)
                    ctx.Report(Id, $"Signalled the {(right ? "right" : "left")} turn", DecisionClass.Safe, ViolationSeverity.None);
                else
                    ctx.Report(Id, $"Turned {(right ? "right" : "left")} without signalling", DecisionClass.Borderline, ViolationSeverity.Minor,
                        null, "Indicate before you turn so pedestrians and moto-taxi riders know what you will do.");
            }
        }
    }

    /// <summary>Head-yaw blind-spot / mirror check before a manoeuvre, measured from head tracking.</summary>
    public class BlindSpotRule : DriverRule
    {
        const float k_Lookback = 2f;

        BlindSpotCheckZone m_Zone;
        float m_EntryTime;
        Vector3 m_EntryForward;

        public override string Id => "blind_spot";

        public override void Begin(RuleContext ctx) => m_Zone = null;

        public override void Tick(RuleContext ctx, float dt)
        {
            var driver = ctx.driver;
            BlindSpotCheckZone inside = null;
            foreach (var zone in BlindSpotCheckZone.All)
                if (zone.Contains(driver.Position)) { inside = zone; break; }

            if (inside != null && m_Zone == null)
            {
                m_Zone = inside;
                m_EntryTime = Time.time;
                m_EntryForward = driver.Forward;
            }
            else if (inside == null && m_Zone != null)
            {
                ctx.gaze.MaxYawSince(m_EntryForward, m_EntryTime - k_Lookback, out var left, out var right);
                var yaw = m_Zone.Side == TurnSide.Left ? left : right;
                var side = m_Zone.Side == TurnSide.Left ? "left" : "right";
                if (yaw >= ctx.scoring.blindSpotCheckYawDegrees)
                    ctx.Report(Id, $"Checked the {side} blind spot", DecisionClass.Safe, ViolationSeverity.None);
                else
                    ctx.Report(Id, $"Did not check the {side} blind spot", DecisionClass.Borderline, ViolationSeverity.Minor,
                        $"Largest head turn to the {side}: {yaw:0} degrees.",
                        "Moto-taxis filter into gaps beside you. Turn your head over your shoulder before you turn or change lane.");
                m_Zone = null;
            }
        }
    }

    /// <summary>Leaving the carriageway (mounting the pavement, cutting corners off-road).</summary>
    public class OffRoadRule : DriverRule
    {
        float m_OffTime;
        bool m_Reported;
        bool m_HasZones;

        public override string Id => "off_road";

        public override void Begin(RuleContext ctx)
        {
            m_OffTime = 0f;
            m_Reported = false;
            m_HasZones = false;
            foreach (var zone in RoadZone.All)
                if (zone.IsDrivable) { m_HasZones = true; break; }
        }

        public override void Tick(RuleContext ctx, float dt)
        {
            if (!m_HasZones || ctx.driver.SpeedKmh < 1f)
                return;
            if (RoadZone.IsOnDrivable(ctx.driver.Position))
            {
                m_OffTime = 0f;
                m_Reported = false;
                return;
            }

            m_OffTime += dt;
            if (m_OffTime < ctx.scoring.offRoadGraceSeconds || m_Reported)
                return;
            m_Reported = true;
            var sidewalk = RoadZone.FindFirst(ctx.driver.Position, z => z.Type == RoadZoneType.Sidewalk) != null;
            ctx.Report(Id, sidewalk ? "Drove onto the pavement" : "Left the carriageway", DecisionClass.Unsafe, ViolationSeverity.Major,
                null, "Pavements belong to pedestrians. Slow down before turns so you can stay in your lane.");
        }
    }
}
