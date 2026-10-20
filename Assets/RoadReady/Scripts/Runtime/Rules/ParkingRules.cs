using System.Collections.Generic;
using RoadReady.Core;
using RoadReady.Scenarios;
using UnityEngine;

namespace RoadReady.Rules
{
    /// <summary>
    /// Reverse bay parking, scored like the RNP test yard (start at 100, points off per fault, pass at 80):
    /// speed over the car-park limit, selecting / leaving reverse while moving, no mirror + over-the-shoulder check
    /// before reversing, tyres on the bay lines, cones, kerb strikes and too many corrections. Finishing needs all
    /// four wheels in the bay, the car straight, stopped and secured (P or handbrake). It also starts the
    /// pedestrian who walks behind the car once it reverses; that hazard's reaction time is scored by the hazard
    /// system, and hitting the pedestrian fails the attempt.
    /// </summary>
    public class ParkingRule : DriverRule
    {
        enum Look { None, LeftMirror, RightMirror, RearMirror, LeftShoulder, RightShoulder }

        // Head yaw windows relative to the car (degrees, + = right) for a left-hand-drive seat.
        static readonly Vector2 k_LeftMirror = new Vector2(-60f, -28f);
        static readonly Vector2 k_RightMirror = new Vector2(30f, 70f);
        static readonly Vector2 k_LeftShoulder = new Vector2(-180f, -100f);
        static readonly Vector2 k_RightShoulder = new Vector2(100f, 180f);
        static readonly Vector2 k_RearMirror = new Vector2(5f, 40f); // interior mirror: up and to the right
        const float k_RearMirrorPitch = 5f;
        const float k_MinLookSeconds = 0.3f;
        const float k_ContactCooldown = 1.5f;

        readonly float[] m_LastCheck = new float[6];
        Look m_Pending;
        float m_LookStart;

        RuleContext m_Ctx;
        Gear m_LastGear;
        int m_LastDirection;
        int m_Corrections;
        bool m_OverSpeedCharged;
        bool m_HazardFired;
        bool m_CheckedBeforeReverse;
        float m_Settled;
        float m_DrySteering;
        float m_MaxSpeed;
        readonly Dictionary<string, float> m_LastContact = new Dictionary<string, float>();

        public override string Id => "parking";

        /// <summary>The rules for a parking attempt: collisions plus the parking rule (no road rules apply in a car park).</summary>
        public static List<RoadRule> CreateRules() => new List<RoadRule> { new CollisionRule(), new ParkingRule() };

        public override void Begin(RuleContext ctx)
        {
            m_Ctx = ctx;
            for (var i = 0; i < m_LastCheck.Length; i++) m_LastCheck[i] = -999f;
            m_Pending = Look.None;
            m_LastGear = ctx.driver.Gear;
            m_LastDirection = 0;
            m_Corrections = 0;
            m_OverSpeedCharged = m_HazardFired = m_CheckedBeforeReverse = false;
            m_Settled = m_DrySteering = m_MaxSpeed = 0f;
            m_LastContact.Clear();
            if (ctx.parking != null)
                ctx.parking.Completed = false;
            ctx.driver.ObstacleHit += OnObstacleHit;
        }

        public override void End(RuleContext ctx)
        {
            ctx.driver.ObstacleHit -= OnObstacleHit;
            m_Ctx = null;
        }

        public override void Tick(RuleContext ctx, float dt)
        {
            var parking = ctx.parking;
            if (parking == null || parking.Completed)
                return;
            var car = ctx.driver;
            var s = ctx.scoring;
            var signedKmh = car.SignedSpeed * Units.MsToKmh;
            var speedKmh = Mathf.Abs(signedKmh);
            m_MaxSpeed = Mathf.Max(m_MaxSpeed, speedKmh);
            UpdateObservation(ctx);

            // Gear changes: reverse selected or left while moving; all-round check before reversing.
            if (car.Gear != m_LastGear)
            {
                if (speedKmh > 2f && (car.Gear == Gear.Reverse || m_LastGear == Gear.Reverse))
                    Fault(ctx, "parking_gear", "Selected or left reverse while still moving", DecisionClass.Unsafe, ViolationSeverity.Minor, s.parkingWrongGearPoints,
                        $"Moving at {speedKmh:0} km/h.", "Stop completely before selecting or leaving reverse.");

                if (car.Gear == Gear.Reverse)
                {
                    if (AllRoundCheckWithin(ctx.Now, s.parkingCheckWindowSeconds))
                    {
                        if (!m_CheckedBeforeReverse)
                            ctx.Report("parking_observation", "Checked the mirrors and over your shoulder before reversing", DecisionClass.Safe, ViolationSeverity.None);
                        m_CheckedBeforeReverse = true;
                    }
                    else
                    {
                        Fault(ctx, "parking_observation", "No all-round check before reversing", DecisionClass.Unsafe, ViolationSeverity.Major, s.parkingNoObservationPoints,
                            $"Within {s.parkingCheckWindowSeconds:0} s of selecting reverse, look in the mirrors (both side mirrors, or the interior mirror) and over a shoulder.",
                            "Before reversing, check all round: mirrors and over your shoulder for people walking behind the car.");
                    }
                }

                m_LastGear = car.Gear;
            }

            // Speed in the car park (charged once).
            if (!m_OverSpeedCharged && speedKmh > s.parkingSpeedLimitKmh)
            {
                m_OverSpeedCharged = true;
                Fault(ctx, "parking_speed", $"Over {s.parkingSpeedLimitKmh:0} km/h in the car park", DecisionClass.Borderline, ViolationSeverity.Minor, s.parkingOverSpeedPoints,
                    null, "Manoeuvre at walking pace: it gives you time to correct and to stop for people.");
            }

            // Corrections: each change of travel direction beyond the free ones.
            var direction = signedKmh > 1f ? 1 : signedKmh < -1f ? -1 : 0;
            if (direction != 0 && m_LastDirection != 0 && direction != m_LastDirection)
            {
                m_Corrections++;
                if (m_Corrections > s.parkingFreeCorrections)
                    Fault(ctx, "parking_correction", $"Extra correction (#{m_Corrections})", DecisionClass.Borderline, ViolationSeverity.Minor, s.parkingExtraCorrectionPoints,
                        $"{s.parkingFreeCorrections} direction changes are free.", "Line the car up before you start reversing so the bay needs fewer corrections.");
            }

            if (direction != 0)
                m_LastDirection = direction;

            // Dry steering (turning the wheel while stopped): logged for the instructor, no points.
            if (speedKmh < 0.3f && Mathf.Abs(car.Steer) > 0.05f)
                m_DrySteering += dt;

            // Tyres on the bay lines, body against cones.
            var line = parking.LineUnderTyre(car);
            if (line >= 0)
                Contact(ctx, "line" + line, "parking_line", "A tyre touched a bay line", s.parkingLineOrConePoints,
                    "Use the side mirrors to keep an even gap to both lines while reversing.");
            var cone = parking.ConeTouching(car);
            if (cone >= 0)
                Contact(ctx, "cone" + cone, "parking_cone", "Touched a cone", s.parkingLineOrConePoints,
                    "Go slower near the cones and check the mirror on that side.");

            // The pedestrian starts walking behind the car once it is actually reversing.
            if (!m_HazardFired && car.Gear == Gear.Reverse && signedKmh < -1f && parking.PedestrianHazard != null)
            {
                m_HazardFired = true;
                parking.PedestrianHazard.Fire();
            }

            // Finished: all four wheels in the bay, straight, stopped and secured (P or handbrake).
            var angle = parking.AngleToBay(car.transform);
            var secured = car.Handbrake || car.Gear == Gear.Park;
            if (parking.AllWheelsInside(car) && angle <= s.parkingMaxAngleDegrees && speedKmh < 0.5f && secured)
            {
                m_Settled += dt;
                if (m_Settled >= s.parkingSettleSeconds)
                {
                    parking.Completed = true;
                    ctx.Report(Id, "Parked inside the bay", DecisionClass.Safe, ViolationSeverity.None,
                        $"Angle to the bay {angle:0} deg, {m_Corrections} direction change(s), top speed {m_MaxSpeed:0} km/h, dry steering {m_DrySteering:0.0} s.");
                }
            }
            else
            {
                m_Settled = 0f;
            }
        }

        void OnObstacleHit(Collider obstacle)
        {
            if (m_Ctx == null || obstacle.GetComponentInParent<ParkingKerb>() == null)
                return;
            Contact(m_Ctx, "kerb" + obstacle.GetInstanceID(), "parking_kerb", "Hit the kerb", m_Ctx.scoring.parkingKerbPoints,
                "Reverse slowly as you near the back of the bay and stop as soon as the car is fully in.", DecisionClass.Unsafe);
        }

        void Contact(RuleContext ctx, string key, string ruleId, string title, float points, string tip, DecisionClass decision = DecisionClass.Borderline)
        {
            if (m_LastContact.TryGetValue(key, out var last) && ctx.Now - last < k_ContactCooldown)
            {
                m_LastContact[key] = ctx.Now; // still touching: keep it as one touch
                return;
            }

            m_LastContact[key] = ctx.Now;
            Fault(ctx, ruleId, title, decision, ViolationSeverity.Minor, points, null, tip);
        }

        static void Fault(RuleContext ctx, string ruleId, string title, DecisionClass decision, ViolationSeverity severity, float points, string detail, string tip)
        {
            var record = ctx.Report(ruleId, title, decision, severity, detail, tip);
            record.points = points;
        }

        // ------------------------------------------------------------------ observation (mirrors, shoulders)

        void UpdateObservation(RuleContext ctx)
        {
            var head = ctx.gaze != null ? ctx.gaze.Head : null;
            if (head == null)
                return;
            var local = ctx.driver.transform.InverseTransformDirection(head.forward);
            var yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            var pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;

            var now = Look.None;
            if (In(yaw, k_LeftShoulder)) now = Look.LeftShoulder;
            else if (In(yaw, k_RightShoulder)) now = Look.RightShoulder;
            else if (In(yaw, k_RearMirror) && pitch > k_RearMirrorPitch) now = Look.RearMirror;
            else if (In(yaw, k_LeftMirror)) now = Look.LeftMirror;
            else if (In(yaw, k_RightMirror)) now = Look.RightMirror;

            if (now != m_Pending)
            {
                m_Pending = now;
                m_LookStart = ctx.Now;
            }

            if (m_Pending != Look.None && ctx.Now - m_LookStart >= k_MinLookSeconds)
                m_LastCheck[(int)m_Pending] = ctx.Now;
        }

        bool Checked(Look look, float now, float seconds) => now - m_LastCheck[(int)look] <= seconds;

        /// <summary>Both side mirrors (or the interior mirror) and at least one over-the-shoulder look.</summary>
        bool AllRoundCheckWithin(float now, float seconds) =>
            (Checked(Look.RearMirror, now, seconds) || Checked(Look.LeftMirror, now, seconds) && Checked(Look.RightMirror, now, seconds))
            && (Checked(Look.LeftShoulder, now, seconds) || Checked(Look.RightShoulder, now, seconds));

        static bool In(float value, Vector2 range) => value >= range.x && value <= range.y;
    }
}
