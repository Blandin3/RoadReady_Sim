using System;
using System.Collections.Generic;
using RoadReady.Config;
using RoadReady.Core;
using UnityEngine;

namespace RoadReady.Traffic
{
    [Serializable]
    public class VehiclePrefabEntry
    {
        public RoadUserKind kind = RoadUserKind.Car;
        public TrafficVehicleAI prefab;
    }

    /// <summary>
    /// Seeded, pooled background traffic on one path. Density, mix, speed and compliance come from
    /// <see cref="ScenarioParameters"/> so the admin panel controls them (FR11).
    /// </summary>
    public class TrafficSpawner : MonoBehaviour
    {
        [SerializeField] WaypointPath m_Path;
        [SerializeField] List<VehiclePrefabEntry> m_Prefabs = new List<VehiclePrefabEntry>();
        [Tooltip("Multiplies the scenario density for this spawner (e.g. 0.5 for a quiet side road).")]
        [SerializeField] float m_DensityScale = 1f;
        [Tooltip("Vehicles placed along the path when the attempt starts so the road is not empty.")]
        [SerializeField] int m_PrewarmCount = 3;
        [SerializeField] float m_SpawnClearance = 12f;
        [Tooltip("Upper limit of vehicles on this path at once. Needed on looped paths, where vehicles never reach an end and despawn.")]
        [SerializeField] int m_MaxActive = 1000;

        readonly List<TrafficVehicleAI> m_Pool = new List<TrafficVehicleAI>();
        ScenarioParameters m_Parameters;
        float m_NextSpawn = float.PositiveInfinity;
        bool m_Running;

        public WaypointPath Path { get => m_Path; set => m_Path = value; }
        public List<VehiclePrefabEntry> Prefabs => m_Prefabs;
        public float DensityScale { get => m_DensityScale; set => m_DensityScale = value; }
        public int PrewarmCount { get => m_PrewarmCount; set => m_PrewarmCount = value; }
        public int MaxActive { get => m_MaxActive; set => m_MaxActive = value; }

        int ActiveCount
        {
            get
            {
                var n = 0;
                foreach (var v in m_Pool)
                    if (v != null && v.gameObject.activeSelf) n++;
                return n;
            }
        }

        public void Configure(ScenarioParameters parameters)
        {
            m_Parameters = parameters;
        }

        public void StartSpawning()
        {
            StopSpawning();
            if (m_Parameters == null || m_Path == null || m_Prefabs.Count == 0)
                return;
            m_Running = true;

            var interval = Interval();
            if (float.IsInfinity(interval))
                return;
            var spacing = Mathf.Max(m_SpawnClearance * 1.5f, m_Parameters.npcCruiseSpeedKmh * Units.KmhToMs * interval);
            if (m_Path.Loop)
            {
                // Spread evenly around the loop; nothing ever drives off the end, so the loop starts full.
                var count = Mathf.Min(m_PrewarmCount, m_MaxActive);
                for (var i = 0; i < count; i++)
                {
                    var d = m_Path.Length * (i + 0.5f) / Mathf.Max(1, count);
                    if (IsClear(m_Path.GetPoint(d), 20f))
                        Spawn(d);
                }
            }
            else
            {
                for (var i = 0; i < m_PrewarmCount; i++)
                {
                    var d = m_Path.Length * 0.15f + i * spacing;
                    if (d > m_Path.Length * 0.85f)
                        break;
                    Spawn(d);
                }
            }

            m_NextSpawn = Time.time + interval * RoadReadyRandom.Range(0.3f, 1f);
        }

        public void StopSpawning()
        {
            m_Running = false;
            m_NextSpawn = float.PositiveInfinity;
            foreach (var v in m_Pool)
                if (v != null) v.gameObject.SetActive(false);
        }

        float Interval()
        {
            var perMinute = m_Parameters.trafficDensityPerMinute * m_DensityScale;
            return perMinute <= 0.01f ? float.PositiveInfinity : 60f / perMinute;
        }

        void Update()
        {
            if (!m_Running || Time.time < m_NextSpawn)
                return;
            if (ActiveCount < m_MaxActive && IsSpawnPointClear())
                Spawn(0f);
            m_NextSpawn = Time.time + Interval() * RoadReadyRandom.Range(0.6f, 1.4f);
        }

        bool IsSpawnPointClear() => IsClear(m_Path.GetPoint(0f), m_SpawnClearance);

        static bool IsClear(Vector3 point, float clearance)
        {
            foreach (var agent in RoadAgentRegistry.All)
                if ((agent.Position - point).sqrMagnitude < clearance * clearance)
                    return false;
            return true;
        }

        void Spawn(float distance)
        {
            var kind = PickKind();
            var prefab = FindPrefab(kind);
            if (prefab == null)
                return;

            var vehicle = m_Pool.Find(v => v != null && !v.gameObject.activeSelf && v.Kind == kind && v.name.StartsWith(prefab.name + "#"));
            if (vehicle == null)
            {
                vehicle = Instantiate(prefab, transform);
                vehicle.name = prefab.name + "#" + m_Pool.Count;
                m_Pool.Add(vehicle);
            }

            var speedJitter = RoadReadyRandom.Range(0.85f, 1.15f);
            var speed = m_Parameters.npcCruiseSpeedKmh * speedJitter * (kind == RoadUserKind.Bicycle ? 0.4f : kind == RoadUserKind.Bus ? 0.85f : 1f);
            var obeys = RoadReadyRandom.Chance(m_Parameters.signalCompliance);
            var yields = RoadReadyRandom.Chance(m_Parameters.driverYieldCompliance);
            vehicle.SetDormant(false, false);
            vehicle.gameObject.SetActive(true);
            vehicle.Configure(m_Path, distance, speed, obeys, yields);
            if (kind == RoadUserKind.MotoTaxi)
                vehicle.SetWeaving(RoadReadyRandom.Chance(0.4f));
        }

        RoadUserKind PickKind()
        {
            var roll = RoadReadyRandom.Value;
            if (roll < m_Parameters.motoTaxiShare) return RoadUserKind.MotoTaxi;
            roll -= m_Parameters.motoTaxiShare;
            if (roll < m_Parameters.busShare) return RoadUserKind.Bus;
            roll -= m_Parameters.busShare;
            if (roll < m_Parameters.bicycleShare) return RoadUserKind.Bicycle;
            return RoadUserKind.Car;
        }

        TrafficVehicleAI FindPrefab(RoadUserKind kind)
        {
            // Several models can share a kind (e.g. sedan, hatchback and jeep cars): pick one at random.
            var matches = m_Prefabs.FindAll(e => e.kind == kind && e.prefab != null);
            if (matches.Count > 0)
                return matches[RoadReadyRandom.Range(0, matches.Count)].prefab;
            return m_Prefabs.Find(e => e.prefab != null)?.prefab;
        }
    }

}
