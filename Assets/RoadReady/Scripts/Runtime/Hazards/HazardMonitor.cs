using System;
using System.Collections.Generic;
using RoadReady.Config;
using RoadReady.Core;
using RoadReady.Data;
using RoadReady.Player;
using RoadReady.Traffic;
using UnityEngine;

namespace RoadReady.Hazards
{
    /// <summary>
    /// Tracks every scored hazard from onset to resolution (FR6/FR7): perception (head-gaze fixation or the
    /// "I see a hazard" button), appropriate response, minimum time-to-collision and collisions, then
    /// classifies the decision and the situation-awareness failure level.
    /// </summary>
    public class HazardMonitor
    {
        class ActiveHazard
        {
            public HazardTrigger trigger;
            public HazardDefinition definition;
            public PerformanceRecord record;
            public float gazeDwell;
            public bool violatedHold;
            public bool wasStationaryAtOnset;
            public bool wasOnRoadwayAtOnset;
            public float stopPointPrevDistance = float.NegativeInfinity;
        }

        readonly List<ActiveHazard> m_Active = new List<ActiveHazard>();
        readonly List<HazardTrigger> m_Triggers = new List<HazardTrigger>();
        readonly List<PerformanceRecord> m_Resolved = new List<PerformanceRecord>();
        readonly HashSet<IRoadAgent> m_HazardAgents = new HashSet<IRoadAgent>();

        Perspective m_Perspective;
        ScoringConfig m_Scoring;
        DriverController m_Driver;
        PedestrianController m_Pedestrian;
        GazeTracker m_Gaze;
        IRoadAgent m_Player;
        TelemetryWriter m_Telemetry;
        int m_Counter;

        public event Action<PerformanceRecord, DecisionRecord> HazardResolved;
        public event Action<HazardTrigger> HazardOnset;

        public IReadOnlyList<PerformanceRecord> Records => m_Resolved;
        public int ActiveCount => m_Active.Count;

        public void Begin(Perspective perspective, ScoringConfig scoring, IEnumerable<HazardTrigger> triggers, IRoadAgent player,
            DriverController driver, PedestrianController pedestrian, GazeTracker gaze, TelemetryWriter telemetry)
        {
            m_Perspective = perspective;
            m_Scoring = scoring;
            m_Player = player;
            m_Driver = driver;
            m_Pedestrian = pedestrian;
            m_Gaze = gaze;
            m_Telemetry = telemetry;
            m_Active.Clear();
            m_Resolved.Clear();
            m_HazardAgents.Clear();
            m_Triggers.Clear();
            m_Triggers.AddRange(triggers);
            m_Counter = 0;
            foreach (var trigger in m_Triggers)
                trigger.Onset += OnTriggerOnset;
            if (m_Pedestrian != null)
                m_Pedestrian.StopPressed += OnStopPressed;
            RoadReadyEvents.HazardSpottedPressed += OnHazardSpotted;
        }

        public void End()
        {
            foreach (var hazard in m_Active.ToArray())
                Resolve(hazard);
            m_Active.Clear();
            foreach (var trigger in m_Triggers)
                trigger.Onset -= OnTriggerOnset;
            if (m_Pedestrian != null)
                m_Pedestrian.StopPressed -= OnStopPressed;
            RoadReadyEvents.HazardSpottedPressed -= OnHazardSpotted;
        }

        /// <summary>True for agents that are (or were) actors of a hazard in this attempt - used to avoid double-penalising.</summary>
        public bool IsHazardActor(IRoadAgent agent) => m_HazardAgents.Contains(agent);

        public void Tick(float dt)
        {
            var now = SimClock.Now;
            foreach (var trigger in m_Triggers)
                trigger.Tick(m_Player, now);

            for (var i = m_Active.Count - 1; i >= 0; i--)
            {
                var hazard = m_Active[i];
                UpdateHazard(hazard, now, dt);
                if (now - hazard.record.onsetTime >= hazard.definition.responseWindowSeconds || hazard.record.collided)
                    Resolve(hazard);
            }
        }

        public void NotifyCollision(IRoadAgent other)
        {
            foreach (var hazard in m_Active)
                if (hazard.trigger.InvolvesAgent(other))
                    hazard.record.collided = true;
        }

        void OnTriggerOnset(HazardTrigger trigger)
        {
            foreach (var agent in trigger.ActorAgents)
                m_HazardAgents.Add(agent);
            HazardOnset?.Invoke(trigger);
            if (!trigger.IsScored)
                return;

            var def = trigger.Definition;
            var hazard = new ActiveHazard
            {
                trigger = trigger,
                definition = def,
                wasStationaryAtOnset = m_Player != null && m_Player.Speed < 1f,
                wasOnRoadwayAtOnset = m_Pedestrian != null && m_Pedestrian.IsOnRoadway,
                record = new PerformanceRecord
                {
                    recordId = $"H{++m_Counter:00}",
                    hazardId = def.hazardId,
                    hazardName = def.displayName,
                    hazardType = def.type,
                    requiredResponse = def.requiredResponse,
                    severity = def.severity,
                    onsetTime = SimClock.Now,
                    otherPerspectiveInsight = def.otherPerspectiveInsight,
                },
            };
            m_Active.Add(hazard);
            m_Telemetry?.WriteEvent("hazard_onset", SimClock.Now, def.hazardId);
        }

        void OnHazardSpotted()
        {
            var now = SimClock.Now;
            foreach (var hazard in m_Active)
                if (hazard.record.spottedPressTime < 0f)
                    hazard.record.spottedPressTime = now;
            m_Telemetry?.WriteEvent("hazard_spotted", now, m_Active.Count > 0 ? m_Active[0].definition.hazardId : "none");
        }

        void OnStopPressed(float time)
        {
            foreach (var hazard in m_Active)
                if (hazard.definition.requiredResponse == RequiredResponse.StopOrRetreat)
                    MarkResponse(hazard, time);
        }

        void UpdateHazard(ActiveHazard hazard, float now, float dt)
        {
            var record = hazard.record;
            var def = hazard.definition;

            // Perception: continuous head-gaze dwell inside the cone.
            if (m_Gaze != null && record.fixationTime < 0f)
            {
                if (m_Gaze.IsLookingAt(hazard.trigger.FocusPosition, def.gazeConeDegrees))
                {
                    hazard.gazeDwell += dt;
                    if (hazard.gazeDwell >= m_Scoring.gazeFixationSeconds)
                        record.fixationTime = now - hazard.gazeDwell;
                }
                else
                {
                    hazard.gazeDwell = 0f;
                }
            }

            // Proximity to the hazard actors.
            foreach (var agent in hazard.trigger.ActorAgents)
            {
                var ttc = RoadAgentRegistry.TimeToCollision(m_Player, agent);
                if (ttc < record.minTimeToCollision)
                    record.minTimeToCollision = Mathf.Min(99f, ttc);
            }

            // Response.
            switch (def.requiredResponse)
            {
                case RequiredResponse.Brake:
                    if (IsBraking()) MarkResponse(hazard, now);
                    break;
                case RequiredResponse.AvoidOrBrake:
                    if (IsBraking() || (m_Driver != null && Mathf.Abs(m_Driver.Steer) > 0.35f)) MarkResponse(hazard, now);
                    break;
                case RequiredResponse.StopBeforePoint:
                    if (IsBraking()) MarkResponse(hazard, now);
                    CheckStopPoint(hazard);
                    break;
                case RequiredResponse.WaitAtKerb:
                    if (m_Pedestrian != null && m_Pedestrian.IsOnRoadway && !hazard.wasOnRoadwayAtOnset)
                        hazard.violatedHold = true;
                    break;
                case RequiredResponse.StopOrRetreat:
                    if (m_Pedestrian != null && (!m_Pedestrian.IsOnRoadway || (m_Pedestrian.Speed < 0.2f && hazard.wasOnRoadwayAtOnset && now - record.onsetTime > 0.3f)))
                        MarkResponse(hazard, now);
                    break;
                case RequiredResponse.LookTowards:
                    if (record.fixationTime >= 0f) MarkResponse(hazard, record.fixationTime);
                    break;
            }
        }

        bool IsBraking()
        {
            if (m_Driver == null)
                return false;
            return m_Driver.Brake > 0.25f || m_Driver.Acceleration < -2f;
        }

        void CheckStopPoint(ActiveHazard hazard)
        {
            var point = hazard.trigger.StopPoint;
            if (point == null || m_Driver == null)
                return;
            var front = m_Driver.Position + m_Driver.Forward * (m_Driver.Length * 0.5f);
            var forward = point.forward;
            forward.y = 0f;
            var d = Vector3.Dot(front - point.position, forward.normalized);
            if (hazard.stopPointPrevDistance < 0f && d >= 0f && !float.IsNegativeInfinity(hazard.stopPointPrevDistance))
                hazard.violatedHold = true;
            hazard.stopPointPrevDistance = d;
        }

        static void MarkResponse(ActiveHazard hazard, float time)
        {
            var record = hazard.record;
            if (record.responded)
                return;
            record.responded = true;
            record.responseTime = Mathf.Max(record.onsetTime, time);
            record.reactionTime = record.responseTime - record.onsetTime;
        }

        void Resolve(ActiveHazard hazard)
        {
            m_Active.Remove(hazard);
            var record = hazard.record;
            var def = hazard.definition;

            // Passive responses: already stopped (driver) or never stepped out (pedestrian).
            if (!record.responded && !hazard.violatedHold)
            {
                var passiveSafe = def.requiredResponse switch
                {
                    // Already stopped and never got into conflict (may legitimately move off once it cleared).
                    RequiredResponse.Brake or RequiredResponse.AvoidOrBrake or RequiredResponse.StopBeforePoint =>
                        hazard.wasStationaryAtOnset && !record.collided && record.minTimeToCollision >= m_Scoring.safeHazardTimeToCollision,
                    RequiredResponse.WaitAtKerb => true,
                    RequiredResponse.StopOrRetreat => !hazard.wasOnRoadwayAtOnset,
                    _ => false,
                };
                if (passiveSafe)
                {
                    var perceivedAt = FirstPerception(record);
                    record.responded = true;
                    record.responseTime = perceivedAt >= 0f ? perceivedAt : record.onsetTime + def.safeReactionSeconds;
                    record.reactionTime = record.responseTime - record.onsetTime;
                    // Waiting without ever looking is lucky rather than skilled.
                    if (perceivedAt < 0f && def.requiredResponse == RequiredResponse.WaitAtKerb)
                        record.reactionTime = -1f;
                }
            }

            record.perceived = record.fixationTime >= 0f || record.spottedPressTime >= 0f || record.responded;
            record.decision = Classify(record, hazard, def);
            record.saOutcome = ClassifySituationAwareness(record, hazard);
            record.feedback = record.decision switch
            {
                DecisionClass.Safe => def.feedbackSafe,
                DecisionClass.Borderline => def.feedbackBorderline,
                _ => def.feedbackUnsafe,
            };

            m_Resolved.Add(record);
            m_Telemetry?.WriteEvent("hazard_resolved", SimClock.Now, def.hazardId, $"{record.decision}|{record.saOutcome}|rt={record.reactionTime:0.00}|ttc={record.minTimeToCollision:0.00}");

            var decision = new DecisionRecord
            {
                source = DecisionSource.Hazard,
                ruleId = def.hazardId,
                title = def.displayName,
                detail = DescribeOutcome(record),
                decision = record.decision,
                severity = record.decision == DecisionClass.Safe ? ViolationSeverity.None : (ViolationSeverity)Mathf.Clamp(def.severity, 1, 3),
                time = record.onsetTime,
                posX = m_Player.Position.x,
                posZ = m_Player.Position.z,
                speedKmh = m_Player.Speed * Units.MsToKmh,
                tip = record.decision == DecisionClass.Safe ? null : record.feedback,
            };
            HazardResolved?.Invoke(record, decision);
        }

        static float FirstPerception(PerformanceRecord record)
        {
            if (record.fixationTime >= 0f && record.spottedPressTime >= 0f)
                return Mathf.Min(record.fixationTime, record.spottedPressTime);
            return record.fixationTime >= 0f ? record.fixationTime : record.spottedPressTime;
        }

        DecisionClass Classify(PerformanceRecord record, ActiveHazard hazard, HazardDefinition def)
        {
            if (record.collided)
                return DecisionClass.Unsafe;
            if (hazard.violatedHold)
                return record.minTimeToCollision >= m_Scoring.safeHazardTimeToCollision ? DecisionClass.Borderline : DecisionClass.Unsafe;
            if (!record.responded)
                return DecisionClass.Unsafe;
            if (record.reactionTime < 0f)
                return DecisionClass.Borderline; // safe outcome without evidence of perception

            if (record.reactionTime <= def.safeReactionSeconds && record.minTimeToCollision >= m_Scoring.safeHazardTimeToCollision)
                return DecisionClass.Safe;
            if (record.reactionTime <= def.borderlineReactionSeconds && record.minTimeToCollision >= m_Scoring.criticalTimeToCollision)
                return DecisionClass.Borderline;
            return DecisionClass.Unsafe;
        }

        static SituationAwarenessOutcome ClassifySituationAwareness(PerformanceRecord record, ActiveHazard hazard)
        {
            if (record.decision == DecisionClass.Safe)
                return SituationAwarenessOutcome.NoFailure;
            var sawIt = record.fixationTime >= 0f || record.spottedPressTime >= 0f;
            var actedCorrectly = record.responded && !hazard.violatedHold;
            if (!sawIt && !actedCorrectly)
                return SituationAwarenessOutcome.PerceptionFailure;
            if (!actedCorrectly || record.collided)
                return sawIt ? SituationAwarenessOutcome.ComprehensionFailure : SituationAwarenessOutcome.PerceptionFailure;
            if (!sawIt && record.reactionTime < 0f)
                return SituationAwarenessOutcome.PerceptionFailure;
            return SituationAwarenessOutcome.ProjectionFailure;
        }

        static string DescribeOutcome(PerformanceRecord r)
        {
            if (r.collided)
                return "Collision.";
            var parts = new List<string>();
            if (r.fixationTime >= 0f)
                parts.Add($"looked after {r.fixationTime - r.onsetTime:0.0}s");
            else if (r.spottedPressTime < 0f)
                parts.Add("never looked at it");
            if (r.spottedPressTime >= 0f)
                parts.Add($"flagged after {r.spottedPressTime - r.onsetTime:0.0}s");
            parts.Add(r.reactionTime >= 0f ? $"responded in {r.reactionTime:0.0}s" : r.responded ? "stayed safe" : "no response");
            if (r.minTimeToCollision < 10f)
                parts.Add($"closest {r.minTimeToCollision:0.0}s from impact");
            return string.Join(", ", parts) + ".";
        }
    }
}
