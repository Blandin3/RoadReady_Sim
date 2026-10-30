using System;
using System.Collections.Generic;
using RoadReady.Config;
using RoadReady.Core;
using RoadReady.Player;
using UnityEngine;

namespace RoadReady.Scenarios
{
    /// <summary>
    /// Short, skippable, unscored tutorial (FR13, NFR usability: complete unaided in under 5 minutes).
    /// Walks through each control with on-screen / voice instructions and checks the learner actually did it.
    /// </summary>
    public class TutorialDirector : MonoBehaviour
    {
        enum Check
        {
            EngineOn,
            DriveToCircle,
            Throttle,
            BrakeToStop,
            SteerBothWays,
            Indicator,
            Mirrors,
            ShoulderCheck,
            LookLeftRight,
            WalkToMarker,
            CrossNow,
            SpotHazard
        }

        class Step
        {
            public string instructionKey;
            public Check check;
            public string objective;
        }

        [Tooltip("Pedestrian tutorial: marker at the kerb the learner walks to.")]
        [SerializeField] Transform m_KerbMarker;
        [SerializeField] float m_MarkerRadius = 1.2f;

        readonly List<Step> m_Steps = new List<Step>();
        Perspective m_Perspective;
        DriverController m_Driver;
        PedestrianController m_Pedestrian;
        GazeTracker m_Gaze;
        int m_Index = -1;
        float m_StepStart;
        bool m_SteeredLeft;
        bool m_SteeredRight;
        bool m_Spotted;
        bool m_CrossPressed;
        bool m_Moved;
        float m_LeftMirrorTime, m_RightMirrorTime, m_RearMirrorTime;
        Vector3 m_ReferenceForward = Vector3.forward;
        TutorialGuide m_Guide;
        int m_GuideTarget;

        public event Action Completed;
        public event Action<string, int, int> StepChanged;

        public Transform KerbMarker { get => m_KerbMarker; set => m_KerbMarker = value; }
        public bool IsRunning => m_Index >= 0 && m_Index < m_Steps.Count;
        /// <summary>Short statement of what the learner has to do right now (shown in the "what to do" panel).</summary>
        public string Objective => IsRunning ? m_Steps[m_Index].objective : null;
        public int StepIndex => m_Index;
        public int StepCount => m_Steps.Count;

        public void Begin(Perspective perspective, DriverController driver, PedestrianController pedestrian, GazeTracker gaze)
        {
            m_Perspective = perspective;
            m_Driver = driver;
            m_Pedestrian = pedestrian;
            m_Gaze = gaze;
            m_Steps.Clear();
            if (perspective == Perspective.Driver)
            {
                Add(InstructionLibrary.TutDriverStart, Check.EngineOn, "Press the glowing ON button on the console");
                Add(InstructionLibrary.TutDriverThrottle, Check.Throttle, "Release the handbrake, select D and pull the right trigger");
                Add(InstructionLibrary.TutDriverBrake, Check.BrakeToStop, "Brake to a stop with the left trigger");
                Add(InstructionLibrary.TutDriverSteer, Check.SteerBothWays, "Steer left and right");
                Add(InstructionLibrary.TutDriverIndicator, Check.Indicator, "Switch on an indicator (Y left, A right)");
                Add(InstructionLibrary.TutDriverMirrors, Check.Mirrors, "Look in the left, right and rear-view mirrors");
                Add(InstructionLibrary.TutDriverMirror, Check.ShoulderCheck, "Turn your head over your shoulder");
                Add(InstructionLibrary.TutDriverCircle, Check.DriveToCircle, "Follow the arrows and drive into the circle");
                Add(InstructionLibrary.TutSpotHazard, Check.SpotHazard, "Press X when you notice something dangerous");
            }
            else
            {
                Add(InstructionLibrary.TutPedLook, Check.LookLeftRight, "Look left, then right, then left again");
                if (m_KerbMarker != null)
                    Add(InstructionLibrary.TutPedWalk, Check.WalkToMarker, "Walk into the circle at the kerb");
                Add(InstructionLibrary.TutSpotHazard, Check.SpotHazard, "Press X when you notice something dangerous");
                Add(InstructionLibrary.TutPedCross, Check.CrossNow, "When the road is clear, press A to cross");
            }

            // Circle on the road ahead with the direction animation in front of it: where to head.
            if (perspective == Perspective.Driver && driver != null)
            {
                m_Guide = gameObject.AddComponent<TutorialGuide>();
                m_Guide.BeginDriving(() => m_Driver.Position, () => m_Driver.Forward);
            }

            RoadReadyEvents.HazardSpottedPressed += OnSpotted;
            if (m_Pedestrian != null)
                m_Pedestrian.CrossNowPressed += OnCrossNow;
            m_Index = -1;
            Next();
        }

        public void Stop()
        {
            RoadReadyEvents.HazardSpottedPressed -= OnSpotted;
            if (m_Pedestrian != null)
                m_Pedestrian.CrossNowPressed -= OnCrossNow;
            m_Index = -1;
            if (m_Guide != null)
            {
                m_Guide.End();
                Destroy(m_Guide);
                m_Guide = null;
            }
        }

        void Add(string key, Check check, string objective) => m_Steps.Add(new Step { instructionKey = key, check = check, objective = objective });

        void OnSpotted() => m_Spotted = true;

        void OnCrossNow(float time) => m_CrossPressed = true;

        void Next()
        {
            m_Index++;
            m_StepStart = Time.time;
            m_SteeredLeft = m_SteeredRight = m_Spotted = m_CrossPressed = m_Moved = false;
            m_LeftMirrorTime = m_RightMirrorTime = m_RearMirrorTime = 0f;
            if (m_Gaze != null)
            {
                // Fixed reference for look checks - the head's own forward moves with the head.
                m_ReferenceForward = m_Gaze.HeadForward;
                m_ReferenceForward.y = 0f;
            }
            if (m_Index >= m_Steps.Count)
            {
                RoadReadyEvents.RequestInstruction(InstructionLibrary.TutDone);
                Stop();
                Completed?.Invoke();
                return;
            }

            var step = m_Steps[m_Index];
            if (step.check == Check.DriveToCircle && m_Guide != null)
                m_GuideTarget = m_Guide.ReachedCount + 2; // two circles
            if (step.check == Check.WalkToMarker && m_KerbMarker != null && m_Pedestrian != null)
            {
                if (m_Guide == null)
                    m_Guide = gameObject.AddComponent<TutorialGuide>();
                m_Guide.BeginWalking(() => m_Pedestrian.Position, m_KerbMarker);
            }

            RoadReadyEvents.RequestInstruction(step.instructionKey);
            StepChanged?.Invoke(step.instructionKey, m_Index, m_Steps.Count);
        }

        void Update()
        {
            if (!IsRunning)
                return;
            // Give the learner time to hear the instruction before a step can complete.
            if (Time.time - m_StepStart < 1.5f)
                return;
            if (IsStepDone(m_Steps[m_Index].check))
                Next();
        }

        bool IsStepDone(Check check)
        {
            switch (check)
            {
                case Check.EngineOn:
                    return m_Driver == null || m_Driver.EngineOn;
                case Check.DriveToCircle:
                    return m_Guide == null || !m_Guide.IsRunning || m_Guide.ReachedCount >= m_GuideTarget;
                case Check.Throttle:
                    return m_Driver != null && m_Driver.SpeedKmh > 10f;
                case Check.BrakeToStop:
                    return m_Driver != null && m_Driver.Brake > 0.3f && m_Driver.SpeedKmh < 1f;
                case Check.SteerBothWays:
                    if (m_Driver == null) return true;
                    m_SteeredLeft |= m_Driver.Steer < -0.5f;
                    m_SteeredRight |= m_Driver.Steer > 0.5f;
                    return m_SteeredLeft && m_SteeredRight;
                case Check.Indicator:
                    return m_Driver != null && m_Driver.Indicator != TurnSide.None;
                case Check.Mirrors:
                    if (m_Driver == null || m_Gaze == null) return true;
                    var glance = CarMirrors.Classify(m_Driver.transform.InverseTransformDirection(m_Gaze.HeadForward));
                    if (glance == CarMirrors.Glance.Left) m_LeftMirrorTime += Time.deltaTime;
                    else if (glance == CarMirrors.Glance.Right) m_RightMirrorTime += Time.deltaTime;
                    else if (glance == CarMirrors.Glance.Rear) m_RearMirrorTime += Time.deltaTime;
                    return m_LeftMirrorTime > 0.3f && m_RightMirrorTime > 0.3f && m_RearMirrorTime > 0.3f;
                case Check.ShoulderCheck:
                    if (m_Driver == null || m_Gaze == null) return true;
                    return Mathf.Abs(m_Gaze.YawRelativeTo(m_Driver.Forward)) > 55f;
                case Check.LookLeftRight:
                    if (m_Gaze == null) return true;
                    m_Gaze.MaxYawSince(m_ReferenceForward, m_StepStart, out var left, out var right);
                    return left > 45f && right > 45f;
                case Check.WalkToMarker:
                    if (m_Pedestrian == null || m_KerbMarker == null) return true;
                    var d = m_Pedestrian.Position - m_KerbMarker.position;
                    d.y = 0f;
                    m_Moved |= d.magnitude < m_MarkerRadius;
                    return m_Moved;
                case Check.CrossNow:
                    return m_CrossPressed || (m_Pedestrian != null && m_Pedestrian.HasCompletedCrossing);
                case Check.SpotHazard:
                    return m_Spotted;
            }

            return true;
        }
    }
}
