using System;
using System.Collections.Generic;
using RoadReady.Core;
using RoadReady.Player;
using RoadReady.Scenarios;
using RoadReady.Traffic;
using UnityEngine;

namespace RoadReady.Hazards
{
    /// <summary>
    /// Makes the open-road loop "endless": once the learner has driven past a hazard station, its actors are put
    /// back and - at random - its triggers are re-armed for the next lap. Each onset is scored like any other
    /// hazard, so a long drive meets a different mix of hazards every lap.
    /// </summary>
    public class RecurringHazardDirector : MonoBehaviour
    {
        [Serializable]
        public class Station
        {
            public string name;
            [Tooltip("Where the hazard happens (projected onto the loop to know when the driver has passed it).")]
            public Transform anchor;
            public List<HazardTrigger> triggers = new List<HazardTrigger>();
            [Tooltip("TrafficVehicleAI / PedestrianAI actors reset to their start pose before the station re-arms.")]
            public List<Component> actors = new List<Component>();
            [Range(0f, 1f)] public float chancePerLap = 0.65f;

            [NonSerialized] public float loopDistance;
            [NonSerialized] public bool handledThisPass;
        }

        [Tooltip("The lane the learner drives around; used to measure progress along the loop.")]
        [SerializeField] WaypointPath m_Loop;
        [Tooltip("How far past a station the driver must be before it is reset (out of sight in the mirrors).")]
        [SerializeField] float m_ResetAfterMeters = 70f;
        [SerializeField] List<Station> m_Stations = new List<Station>();

        DriverController m_Driver;

        public WaypointPath Loop { get => m_Loop; set => m_Loop = value; }
        public List<Station> Stations => m_Stations;

        void Start()
        {
            var context = GetComponentInParent<ScenarioContext>();
            if (context != null)
                m_Driver = context.PlayerVehicle;
            if (m_Loop == null)
                return;
            foreach (var s in m_Stations)
                if (s.anchor != null)
                    s.loopDistance = m_Loop.GetClosestDistance(s.anchor.position, out _);
        }

        void Update()
        {
            if (m_Loop == null || m_Driver == null || !m_Driver.isActiveAndEnabled)
                return;
            var length = m_Loop.Length;
            if (length <= 0f)
                return;

            var progress = m_Loop.GetClosestDistance(m_Driver.Transform.position, out _);
            foreach (var s in m_Stations)
            {
                // Triggers are only armed during a driver attempt.
                if (s.triggers.Count == 0 || s.triggers[0] == null || !s.triggers[0].IsArmed)
                    continue;

                var pastBy = Mathf.Repeat(progress - s.loopDistance, length);
                if (pastBy > m_ResetAfterMeters && pastBy < m_ResetAfterMeters + 150f)
                {
                    if (s.handledThisPass)
                        continue;
                    s.handledThisPass = true;
                    if (!AnyFired(s))
                        continue;
                    ResetActors(s);
                    if (RoadReadyRandom.Chance(s.chancePerLap))
                        foreach (var t in s.triggers)
                            if (t != null) t.Rearm();
                }
                else if (pastBy > length - 250f)
                {
                    // Approaching the station again: allow the next pass to be handled.
                    s.handledThisPass = false;
                }
            }
        }

        static bool AnyFired(Station s)
        {
            foreach (var t in s.triggers)
                if (t != null && t.HasFired)
                    return true;
            return false;
        }

        static void ResetActors(Station s)
        {
            foreach (var actor in s.actors)
            {
                if (actor == null) continue;
                // Actors that reached the end of their path may have switched themselves off.
                if (!actor.gameObject.activeSelf)
                    actor.gameObject.SetActive(true);
                if (actor is TrafficVehicleAI vehicle) vehicle.ResetToStart();
                else if (actor is PedestrianAI pedestrian) pedestrian.ResetToStart();
            }
        }
    }
}
