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
            Throttle,
            BrakeToStop,
            SteerBothWays,
            Indicator,
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
        Vector3 m_ReferenceForward = Vector3.forward;

        public event Action Completed;
        public event Action<string, int, int> StepChanged;

        public Transform KerbMarker { get => m_KerbMarker; set => m_KerbMarker = value; }
        public bool IsRunning => m_Index >= 0 && m_Index < m_Steps.Count;
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
                Add(InstructionLibrary.TutDriverThrottle, Check.Throttle);
                Add(InstructionLibrary.TutDriverBrake, Check.BrakeToStop);
                Add(InstructionLibrary.TutDriverSteer, Check.SteerBothWays);
                Add(InstructionLibrary.TutDriverIndicator, Check.Indicator);
                Add(InstructionLibrary.TutDriverMirror, Check.ShoulderCheck);
                Add(InstructionLibrary.TutSpotHazard, Check.SpotHazard);
            }
            else
            {
                Add(InstructionLibrary.TutPedLook, Check.LookLeftRight);
                if (m_KerbMarker != null)
                    Add(InstructionLibrary.TutPedWalk, Check.WalkToMarker);
                Add(InstructionLibrary.TutSpotHazard, Check.SpotHazard);
                Add(InstructionLibrary.TutPedCross, Check.CrossNow);
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
        }

        void Add(string key, Check check) => m_Steps.Add(new Step { instructionKey = key, check = check });

        void OnSpotted() => m_Spotted = true;

        void OnCrossNow(float time) => m_CrossPressed = true;

        void Next()
        {
            m_Index++;
            m_StepStart = Time.time;
            m_SteeredLeft = m_SteeredRight = m_Spotted = m_CrossPressed = m_Moved = false;
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
