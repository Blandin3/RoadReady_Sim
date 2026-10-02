using System;
using System.Collections;
using System.Collections.Generic;
using RoadReady.Config;
using RoadReady.Core;
using RoadReady.Traffic;
using UnityEngine;
using UnityEngine.Events;

namespace RoadReady.Hazards
{
    public enum HazardTriggerMode
    {
        /// <summary>Fires at a fixed attempt time.</summary>
        AtTime,
        /// <summary>Fires when the learner enters the trigger volume.</summary>
        PlayerEntersVolume,
        /// <summary>Fires when the learner comes within a distance of a point.</summary>
        PlayerWithinDistance,
        /// <summary>Fired from code / another trigger's UnityEvent.</summary>
        Manual
    }

    [Serializable]
    public class HazardAction
    {
        [Tooltip("A TrafficVehicleAI or PedestrianAI in the scene.")]
        public Component actor;
        public HazardActorMode mode = HazardActorMode.Activate;
        public float value;
        [Tooltip("Seconds after the trigger fires.")]
        public float delay;

        public IHazardActor Actor => actor as IHazardActor;
    }

    /// <summary>
    /// Scripted hazard injection (FR5). Designers place this in a scenario scene, pick when it fires and which
    /// actors do what; the optional <see cref="HazardDefinition"/> makes it a scored hazard.
    /// Unscored triggers (no definition) are used for choreography, e.g. starting a counterpart vehicle.
    /// </summary>
    public class HazardTrigger : MonoBehaviour
    {
        [SerializeField] HazardDefinition m_Definition;
        [Tooltip("Perspectives in which this trigger runs at all (scored hazards also require the definition's perspective).")]
        [SerializeField] PerspectiveMask m_ActiveFor = PerspectiveMask.Both;

        [Header("When")]
        [SerializeField] HazardTriggerMode m_Mode = HazardTriggerMode.PlayerEntersVolume;
        [SerializeField] float m_Time = 5f;
        [SerializeField] Vector3 m_VolumeSize = new Vector3(8f, 3f, 6f);
        [SerializeField] Transform m_DistancePoint;
        [SerializeField] float m_Distance = 30f;
        [Tooltip("Delay between the trigger condition and hazard onset. Scaled by ScenarioParameters.hazardLeadTimeScale.")]
        [SerializeField] float m_OnsetDelay;
        [Tooltip("Optional extra condition: only fire while this signal group shows the given state (e.g. 'when the light turns green').")]
        [SerializeField] TrafficSignalController m_ConditionSignal;
        [SerializeField] string m_ConditionGroup;
        [SerializeField] SignalState m_ConditionState = SignalState.Green;

        [Header("What")]
        [SerializeField] List<HazardAction> m_Actions = new List<HazardAction>();
        [Tooltip("Where the learner has to look to perceive this hazard. Defaults to the first actor.")]
        [SerializeField] Transform m_FocusPoint;
        [Tooltip("StopBeforePoint responses fail if the learner's front passes this point while the hazard is active.")]
        [SerializeField] Transform m_StopPoint;
        [SerializeField] UnityEvent m_OnTriggered;

        bool m_Armed;
        bool m_Fired;
        float m_LeadScale = 1f;

        public HazardDefinition Definition { get => m_Definition; set => m_Definition = value; }
        public PerspectiveMask ActiveFor { get => m_ActiveFor; set => m_ActiveFor = value; }
        public HazardTriggerMode Mode { get => m_Mode; set => m_Mode = value; }
        public float TriggerTime { get => m_Time; set => m_Time = value; }
        public Vector3 VolumeSize { get => m_VolumeSize; set => m_VolumeSize = value; }
        public Transform DistancePoint { get => m_DistancePoint; set => m_DistancePoint = value; }
        public float Distance { get => m_Distance; set => m_Distance = value; }
        public float OnsetDelay { get => m_OnsetDelay; set => m_OnsetDelay = value; }
        public List<HazardAction> Actions => m_Actions;

        public void SetSignalCondition(TrafficSignalController signal, string group, SignalState state)
        {
            m_ConditionSignal = signal;
            m_ConditionGroup = group;
            m_ConditionState = state;
        }
        public Transform FocusPointOverride { get => m_FocusPoint; set => m_FocusPoint = value; }
        public Transform StopPoint { get => m_StopPoint; set => m_StopPoint = value; }

        public bool IsScored => m_Definition != null;
        public bool IsArmed => m_Armed;
        public bool HasFired => m_Fired;

        /// <summary>Raised at hazard onset (after the onset delay).</summary>
        public event Action<HazardTrigger> Onset;

        public Vector3 FocusPosition
        {
            get
            {
                if (m_FocusPoint != null)
                    return m_FocusPoint.position;
                foreach (var action in m_Actions)
                    if (action.Actor != null)
                        return action.Actor.FocusPoint.position + Vector3.up * 0.8f;
                return transform.position;
            }
        }

        public IEnumerable<IRoadAgent> ActorAgents
        {
            get
            {
                foreach (var action in m_Actions)
                    if (action.Actor?.Agent != null)
                        yield return action.Actor.Agent;
            }
        }

        public bool InvolvesAgent(IRoadAgent agent)
        {
            foreach (var a in ActorAgents)
                if (a == agent)
                    return true;
            return false;
        }

        public void Arm(ScenarioParameters parameters, Perspective perspective)
        {
            m_Fired = false;
            m_LeadScale = parameters != null ? parameters.hazardLeadTimeScale : 1f;
            m_Armed = m_ActiveFor.Includes(perspective);
            if (m_Armed && m_Definition != null)
                m_Armed = m_Definition.perspective == perspective && (parameters == null || parameters.IsHazardEnabled(m_Definition.hazardId));
        }

        public void Disarm() => m_Armed = false;

        /// <summary>Evaluates the trigger condition. Returns true on the frame it fires.</summary>
        public bool Tick(IRoadAgent player, float attemptTime)
        {
            if (!m_Armed || m_Fired || player == null)
                return false;
            if (m_ConditionSignal != null && m_ConditionSignal.GetState(m_ConditionGroup) != m_ConditionState)
                return false;

            var fire = m_Mode switch
            {
                HazardTriggerMode.AtTime => attemptTime >= m_Time,
                HazardTriggerMode.PlayerEntersVolume => ContainsPoint(player.Position),
                HazardTriggerMode.PlayerWithinDistance => Vector3.Distance(player.Position, (m_DistancePoint != null ? m_DistancePoint : transform).position) <= m_Distance,
                _ => false,
            };
            if (fire)
                Fire();
            return fire;
        }

        public void Fire()
        {
            if (m_Fired)
                return;
            m_Fired = true;
            StartCoroutine(RunActions());
        }

        IEnumerator RunActions()
        {
            var onsetDelay = m_OnsetDelay * m_LeadScale;
            var start = Time.time;
            var sorted = new List<HazardAction>(m_Actions);
            sorted.Sort((a, b) => a.delay.CompareTo(b.delay));
            var onsetRaised = false;
            foreach (var action in sorted)
            {
                var when = start + action.delay * m_LeadScale;
                if (!onsetRaised && start + onsetDelay <= when)
                {
                    while (Time.time < start + onsetDelay) yield return null;
                    RaiseOnset();
                    onsetRaised = true;
                }

                while (Time.time < when) yield return null;
                action.Actor?.PerformHazard(action.mode, action.value);
            }

            if (!onsetRaised)
            {
                while (Time.time < start + onsetDelay) yield return null;
                RaiseOnset();
            }
        }

        void RaiseOnset()
        {
            m_OnTriggered?.Invoke();
            Onset?.Invoke(this);
        }

        bool ContainsPoint(Vector3 worldPosition)
        {
            var local = transform.InverseTransformPoint(worldPosition);
            var half = m_VolumeSize * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z && local.y >= -1f && local.y <= m_VolumeSize.y;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = IsScored ? new Color(1f, 0.2f, 0.2f, 0.9f) : new Color(0.7f, 0.4f, 1f, 0.9f);
            if (m_Mode == HazardTriggerMode.PlayerEntersVolume)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireCube(new Vector3(0f, m_VolumeSize.y * 0.5f, 0f), m_VolumeSize);
                Gizmos.matrix = Matrix4x4.identity;
            }
            else if (m_Mode == HazardTriggerMode.PlayerWithinDistance)
            {
                Gizmos.DrawWireSphere((m_DistancePoint != null ? m_DistancePoint : transform).position, m_Distance);
            }

            foreach (var action in m_Actions)
                if (action.actor != null)
                    Gizmos.DrawLine(transform.position + Vector3.up, action.actor.transform.position + Vector3.up);
        }
    }
}
