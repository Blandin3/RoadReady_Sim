using System;
using System.Collections.Generic;
using RoadReady.Config;
using RoadReady.Core;
using UnityEngine;

namespace RoadReady.Traffic
{
    /// <summary>Seeded background pedestrians. A share of them use "jaywalk" paths that cross away from crossings.</summary>
    public class PedestrianSpawner : MonoBehaviour
    {
        [SerializeField] List<WaypointPath> m_Paths = new List<WaypointPath>();
        [SerializeField] List<WaypointPath> m_JaywalkPaths = new List<WaypointPath>();
        [SerializeField] List<PedestrianAI> m_Prefabs = new List<PedestrianAI>();
        [SerializeField] float m_DensityScale = 1f;
        [SerializeField] int m_PrewarmCount = 2;

        readonly List<PedestrianAI> m_Pool = new List<PedestrianAI>();
        ScenarioParameters m_Parameters;
        float m_NextSpawn = float.PositiveInfinity;
        bool m_Running;

        public List<WaypointPath> Paths => m_Paths;
        public List<WaypointPath> JaywalkPaths => m_JaywalkPaths;
        public List<PedestrianAI> Prefabs => m_Prefabs;

        public void Configure(ScenarioParameters parameters) => m_Parameters = parameters;

        public void StartSpawning()
        {
            StopSpawning();
            if (m_Parameters == null || m_Paths.Count == 0 || m_Prefabs.Count == 0)
                return;
            m_Running = true;
            for (var i = 0; i < m_PrewarmCount; i++)
                Spawn(RoadReadyRandom.Range(0.1f, 0.5f));
            m_NextSpawn = Time.time + Interval() * RoadReadyRandom.Range(0.3f, 1f);
        }

        public void StopSpawning()
        {
            m_Running = false;
            m_NextSpawn = float.PositiveInfinity;
            foreach (var p in m_Pool)
                if (p != null) p.gameObject.SetActive(false);
        }

        float Interval()
        {
            var perMinute = m_Parameters.pedestrianDensityPerMinute * m_DensityScale;
            return perMinute <= 0.01f ? float.PositiveInfinity : 60f / perMinute;
        }

        void Update()
        {
            if (!m_Running || Time.time < m_NextSpawn)
                return;
            Spawn(0f);
            m_NextSpawn = Time.time + Interval() * RoadReadyRandom.Range(0.6f, 1.4f);
        }

        void Spawn(float normalizedStart)
        {
            var jaywalk = m_JaywalkPaths.Count > 0 && RoadReadyRandom.Chance(m_Parameters.pedestrianJaywalkRate);
            var paths = jaywalk ? m_JaywalkPaths : m_Paths;
            var path = paths[RoadReadyRandom.Range(0, paths.Count)];
            var prefab = m_Prefabs[RoadReadyRandom.Range(0, m_Prefabs.Count)];
            if (path == null || prefab == null)
                return;

            var ped = m_Pool.Find(p => p != null && !p.gameObject.activeSelf);
            if (ped == null)
            {
                ped = Instantiate(prefab, transform);
                ped.name = prefab.name + "_" + m_Pool.Count;
                m_Pool.Add(ped);
            }

            ped.SetDormant(false, false);
            ped.gameObject.SetActive(true);
            ped.Configure(path, path.Length * normalizedStart, RoadReadyRandom.Range(1.1f, 1.5f), jaywalk && RoadReadyRandom.Chance(0.5f), !jaywalk);
        }
    }
}
