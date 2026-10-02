using System.Collections.Generic;
using RoadReady.Config;
using RoadReady.Core;
using RoadReady.Hazards;
using RoadReady.Player;
using RoadReady.Traffic;
using UnityEngine;

namespace RoadReady.Scenarios
{
    /// <summary>
    /// Root component of every scenario scene. Holds spawn points, the learner's car, traffic, signals and the
    /// dual-perspective counterparts: actors that play the "other" road user so the same situation unfolds
    /// whether the learner is the driver or the pedestrian (proposal 3.5.3).
    /// </summary>
    public class ScenarioContext : MonoBehaviour
    {
        [SerializeField] ScenarioDefinition m_Definition;

        [Header("Spawns")]
        [SerializeField] Transform m_DriverSpawn;
        [SerializeField] Transform m_PedestrianSpawn;
        [SerializeField] DriverController m_PlayerVehicle;

        [Header("Dual perspective counterparts")]
        [Tooltip("Enabled only when the learner drives (e.g. the scripted pedestrian at the zebra).")]
        [SerializeField] List<GameObject> m_ActiveWhenDriving = new List<GameObject>();
        [Tooltip("Enabled only when the learner walks (e.g. the scripted car approaching the zebra).")]
        [SerializeField] List<GameObject> m_ActiveWhenWalking = new List<GameObject>();

        [Header("Environment")]
        [SerializeField] List<TrafficSpawner> m_TrafficSpawners = new List<TrafficSpawner>();
        [SerializeField] List<PedestrianSpawner> m_PedestrianSpawners = new List<PedestrianSpawner>();
        [SerializeField] List<TrafficSignalController> m_Signals = new List<TrafficSignalController>();
        [SerializeField] List<GoalZone> m_Goals = new List<GoalZone>();
        [Tooltip("Signal cycle start offset per perspective (seconds, -1 = keep the controller's own). Lets the same junction start at the phase that makes each perspective's decision meaningful.")]
        [SerializeField] float m_SignalOffsetDriver = -1f;
        [SerializeField] float m_SignalOffsetPedestrian = -1f;
        [Tooltip("Optional: lights / weather objects toggled by ScenarioParameters (time of day, rain).")]
        [SerializeField] GameObject m_DayLighting;
        [SerializeField] GameObject m_DuskLighting;
        [SerializeField] GameObject m_NightLighting;
        [SerializeField] GameObject m_RainEffects;

        readonly List<HazardTrigger> m_Triggers = new List<HazardTrigger>();
        readonly List<TrafficVehicleAI> m_ScriptedVehicles = new List<TrafficVehicleAI>();
        readonly List<PedestrianAI> m_ScriptedPedestrians = new List<PedestrianAI>();

        public ScenarioDefinition Definition { get => m_Definition; set => m_Definition = value; }
        public Transform DriverSpawn { get => m_DriverSpawn; set => m_DriverSpawn = value; }
        public Transform PedestrianSpawn { get => m_PedestrianSpawn; set => m_PedestrianSpawn = value; }
        public DriverController PlayerVehicle { get => m_PlayerVehicle; set => m_PlayerVehicle = value; }
        public List<GameObject> ActiveWhenDriving => m_ActiveWhenDriving;
        public List<GameObject> ActiveWhenWalking => m_ActiveWhenWalking;
        public List<TrafficSpawner> TrafficSpawners => m_TrafficSpawners;
        public List<PedestrianSpawner> PedestrianSpawners => m_PedestrianSpawners;
        public List<TrafficSignalController> Signals => m_Signals;
        public List<GoalZone> Goals => m_Goals;
        public float SignalOffsetDriver { get => m_SignalOffsetDriver; set => m_SignalOffsetDriver = value; }
        public float SignalOffsetPedestrian { get => m_SignalOffsetPedestrian; set => m_SignalOffsetPedestrian = value; }
        public IReadOnlyList<HazardTrigger> Triggers => m_Triggers;
        public TutorialDirector Tutorial { get; private set; }

        public Transform GetSpawn(Perspective perspective) => perspective == Perspective.Driver ? m_DriverSpawn : m_PedestrianSpawn;

        /// <summary>Configure everything for an attempt. Traffic starts running so the street is alive during the countdown.</summary>
        public void Prepare(ScenarioParameters parameters, Perspective perspective)
        {
            m_Triggers.Clear();
            m_Triggers.AddRange(GetComponentsInChildren<HazardTrigger>(true));
            Tutorial = GetComponentInChildren<TutorialDirector>(true);

            foreach (var go in m_ActiveWhenDriving)
                if (go != null) go.SetActive(perspective == Perspective.Driver);
            foreach (var go in m_ActiveWhenWalking)
                if (go != null) go.SetActive(perspective == Perspective.Pedestrian);

            if (m_PlayerVehicle != null)
                m_PlayerVehicle.gameObject.SetActive(perspective == Perspective.Driver);

            // Scripted actors are placed by hand; reset them to their authored start state.
            m_ScriptedVehicles.Clear();
            m_ScriptedPedestrians.Clear();
            foreach (var v in GetComponentsInChildren<TrafficVehicleAI>(false))
                if (v.IsScripted) { m_ScriptedVehicles.Add(v); v.ResetToStart(); }
            foreach (var p in GetComponentsInChildren<PedestrianAI>(false))
                if (p.IsScripted) { m_ScriptedPedestrians.Add(p); p.ResetToStart(); }

            foreach (var signal in m_Signals)
            {
                if (signal == null) continue;
                var offset = perspective == Perspective.Driver ? m_SignalOffsetDriver : m_SignalOffsetPedestrian;
                if (offset >= 0f)
                    signal.StartOffset = offset;
                signal.SetCycleScale(parameters.signalCycleScale);
                signal.ResetCycle();
                signal.SetRunning(true);
            }

            foreach (var spawner in m_TrafficSpawners)
            {
                if (spawner == null) continue;
                spawner.Configure(parameters);
                spawner.StartSpawning();
            }

            foreach (var spawner in m_PedestrianSpawners)
            {
                if (spawner == null) continue;
                spawner.Configure(parameters);
                spawner.StartSpawning();
            }

            foreach (var trigger in m_Triggers)
                trigger.Arm(parameters, perspective);

            ApplyEnvironment(parameters);
        }

        void ApplyEnvironment(ScenarioParameters parameters)
        {
            if (m_DayLighting != null) m_DayLighting.SetActive(parameters.timeOfDay == TimeOfDay.Day);
            if (m_DuskLighting != null) m_DuskLighting.SetActive(parameters.timeOfDay == TimeOfDay.Dusk);
            if (m_NightLighting != null) m_NightLighting.SetActive(parameters.timeOfDay == TimeOfDay.Night);
            if (m_RainEffects != null) m_RainEffects.SetActive(parameters.weather == Weather.Rain);
        }

        public void Shutdown()
        {
            foreach (var spawner in m_TrafficSpawners)
                if (spawner != null) spawner.StopSpawning();
            foreach (var spawner in m_PedestrianSpawners)
                if (spawner != null) spawner.StopSpawning();
            foreach (var trigger in m_Triggers)
                trigger.Disarm();
        }

        public bool IsGoalReached(Perspective perspective, IRoadAgent player, PedestrianController pedestrian)
        {
            foreach (var goal in m_Goals)
            {
                if (goal == null || !goal.Perspectives.Includes(perspective) || !goal.Contains(player.Position))
                    continue;
                if (perspective == Perspective.Pedestrian && goal.RequiresCrossing && (pedestrian == null || !pedestrian.HasCompletedCrossing))
                    continue;
                return true;
            }

            return false;
        }
    }
}
