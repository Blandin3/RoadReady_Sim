using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadReady.Traffic
{
    public enum SignalState
    {
        Red,
        Amber,
        Green,
        Off
    }

    /// <summary>
    /// Fixed-time signal plan. Each phase sets the state of named signal groups (e.g. "ns", "ew", "ped").
    /// Pedestrian groups use Green = walk, Amber = flashing / clearing, Red = don't walk.
    /// </summary>
    public class TrafficSignalController : MonoBehaviour
    {
        [Serializable]
        public class GroupState
        {
            public string group;
            public SignalState state;
        }

        [Serializable]
        public class Phase
        {
            public string name;
            [Min(0.5f)] public float duration = 10f;
            public List<GroupState> states = new List<GroupState>();
        }

        [SerializeField] List<Phase> m_Phases = new List<Phase>();
        [Tooltip("Seconds into the cycle at attempt start (lets designers line up hazards with phases).")]
        [SerializeField] float m_StartOffset;
        [SerializeField] bool m_Running = true;

        readonly Dictionary<string, SignalState> m_Current = new Dictionary<string, SignalState>();
        float m_Scale = 1f;
        float m_CycleTime;
        int m_PhaseIndex = -1;

        public event Action<int> PhaseChanged;

        public List<Phase> Phases => m_Phases;
        public float StartOffset { get => m_StartOffset; set => m_StartOffset = value; }
        public int PhaseIndex => m_PhaseIndex;
        public float TimeInPhase { get; private set; }

        public void SetCycleScale(float scale) => m_Scale = Mathf.Max(0.1f, scale);

        public void ResetCycle()
        {
            m_CycleTime = m_StartOffset;
            m_PhaseIndex = -1;
            Evaluate();
        }

        public void SetRunning(bool running) => m_Running = running;

        void OnEnable() => ResetCycle();

        void Update()
        {
            if (!m_Running || m_Phases.Count == 0)
                return;
            m_CycleTime += Time.deltaTime;
            Evaluate();
        }

        void Evaluate()
        {
            if (m_Phases.Count == 0)
                return;
            var cycle = 0f;
            foreach (var p in m_Phases)
                cycle += p.duration * m_Scale;
            var t = Mathf.Repeat(m_CycleTime, cycle);
            var index = 0;
            while (index < m_Phases.Count - 1 && t >= m_Phases[index].duration * m_Scale)
            {
                t -= m_Phases[index].duration * m_Scale;
                index++;
            }

            TimeInPhase = t;
            if (index == m_PhaseIndex)
                return;

            m_PhaseIndex = index;
            m_Current.Clear();
            foreach (var gs in m_Phases[index].states)
                m_Current[gs.group] = gs.state;
            PhaseChanged?.Invoke(index);
        }

        public SignalState GetState(string group) =>
            group != null && m_Current.TryGetValue(group, out var state) ? state : SignalState.Red;

        /// <summary>Seconds remaining until <paramref name="group"/> changes state (approximate, current phase only).</summary>
        public float TimeRemainingInPhase => m_PhaseIndex >= 0 ? m_Phases[m_PhaseIndex].duration * m_Scale - TimeInPhase : 0f;
    }
}
