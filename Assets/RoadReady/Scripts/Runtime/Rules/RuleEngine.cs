using System;
using System.Collections.Generic;
using RoadReady.Config;
using RoadReady.Core;
using RoadReady.Data;
using RoadReady.Player;
using RoadReady.Traffic;
using UnityEngine;

namespace RoadReady.Rules
{
    /// <summary>Everything a rule may read, plus the reporting callback.</summary>
    public class RuleContext
    {
        public Perspective perspective;
        public ScoringConfig scoring;
        public ScenarioParameters parameters;
        public IRoadAgent player;
        public DriverController driver;
        public PedestrianController pedestrian;
        public GazeTracker gaze;
        /// <summary>Agents already evaluated by a scored hazard (to avoid double penalties).</summary>
        public Func<IRoadAgent, bool> isHazardActor;
        /// <summary>Invoked by the collision rule; the scenario manager may end the attempt.</summary>
        public Action<IRoadAgent, string> onCollision;
        public Action<DecisionRecord> onDecision;

        public float Now => SimClock.Now;

        public DecisionRecord Report(string ruleId, string title, DecisionClass decision, ViolationSeverity severity, string detail = null, string tip = null)
        {
            var record = new DecisionRecord
            {
                source = DecisionSource.Rule,
                ruleId = ruleId,
                title = title,
                detail = detail,
                decision = decision,
                severity = decision == DecisionClass.Safe ? ViolationSeverity.None : severity,
                time = Now,
                posX = player != null ? player.Position.x : 0f,
                posZ = player != null ? player.Position.z : 0f,
                speedKmh = player != null ? player.Speed * Units.MsToKmh : 0f,
                tip = tip,
            };
            onDecision?.Invoke(record);
            return record;
        }
    }

    /// <summary>A deterministic traffic rule evaluated every frame (FR6).</summary>
    public abstract class RoadRule
    {
        public abstract string Id { get; }
        public virtual bool AppliesTo(Perspective perspective) => true;
        public virtual void Begin(RuleContext ctx) { }
        public abstract void Tick(RuleContext ctx, float dt);
        public virtual void End(RuleContext ctx) { }
    }

    /// <summary>
    /// The rule-based decision evaluation engine (proposal 3.2.3). New rules are added here without touching
    /// scoring (NFR scalability / maintainability).
    /// </summary>
    public class RuleEngine
    {
        readonly List<RoadRule> m_Rules = new List<RoadRule>();
        RuleContext m_Context;

        public IReadOnlyList<RoadRule> Rules => m_Rules;

        public static List<RoadRule> CreateDefaultRules() => new List<RoadRule>
        {
            // Shared
            new CollisionRule(),
            new ProximityRule(),
            // Driver
            new SpeedLimitRule(),
            new StopLineRule(),
            new FollowingDistanceRule(),
            new TurnSignalRule(),
            new BlindSpotRule(),
            new OffRoadRule(),
            // Pedestrian
            new CrossingDecisionRule(),
            new RoadwayDwellRule(),
        };

        public void Begin(RuleContext context, IEnumerable<RoadRule> rules = null)
        {
            m_Context = context;
            m_Rules.Clear();
            foreach (var rule in rules ?? CreateDefaultRules())
                if (rule.AppliesTo(context.perspective))
                    m_Rules.Add(rule);
            foreach (var rule in m_Rules)
                rule.Begin(context);
        }

        public void Tick(float dt)
        {
            if (m_Context == null)
                return;
            foreach (var rule in m_Rules)
            {
                try
                {
                    rule.Tick(m_Context, dt);
                }
                catch (Exception e)
                {
                    // A faulty rule must never crash an attempt (NFR reliability).
                    Debug.LogException(e);
                }
            }
        }

        public void End()
        {
            if (m_Context == null)
                return;
            foreach (var rule in m_Rules)
                rule.End(m_Context);
            m_Context = null;
        }
    }
}
