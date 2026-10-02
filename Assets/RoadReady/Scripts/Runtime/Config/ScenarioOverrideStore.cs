using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RoadReady.Config
{
    /// <summary>
    /// Persists administrator edits to scenario parameters (FR11) without modifying the shipped assets,
    /// so a build can be re-configured at the pilot site.
    /// </summary>
    public class ScenarioOverrideStore
    {
        [Serializable]
        class Entry
        {
            public string scenarioId;
            public int level;
            public ScenarioParameters parameters;
        }

        [Serializable]
        class FileModel
        {
            public List<Entry> entries = new List<Entry>();
        }

        readonly string m_Path;
        FileModel m_Model = new FileModel();

        public ScenarioOverrideStore(string rootFolder)
        {
            m_Path = Path.Combine(rootFolder, "config", "scenario_overrides.json");
            Load();
        }

        public ScenarioParameters Resolve(ScenarioDefinition scenario, int level)
        {
            var entry = m_Model.entries.Find(e => e.scenarioId == scenario.scenarioId && e.level == level);
            return entry != null ? entry.parameters.Clone() : scenario.GetDefaultParameters(level).Clone();
        }

        public bool HasOverride(ScenarioDefinition scenario, int level) =>
            m_Model.entries.Exists(e => e.scenarioId == scenario.scenarioId && e.level == level);

        public void Set(ScenarioDefinition scenario, int level, ScenarioParameters parameters)
        {
            m_Model.entries.RemoveAll(e => e.scenarioId == scenario.scenarioId && e.level == level);
            m_Model.entries.Add(new Entry { scenarioId = scenario.scenarioId, level = level, parameters = parameters.Clone() });
            Save();
        }

        public void Clear(ScenarioDefinition scenario, int level)
        {
            m_Model.entries.RemoveAll(e => e.scenarioId == scenario.scenarioId && e.level == level);
            Save();
        }

        void Load()
        {
            try
            {
                if (File.Exists(m_Path))
                    m_Model = JsonUtility.FromJson<FileModel>(File.ReadAllText(m_Path)) ?? new FileModel();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RoadReady] Could not read scenario overrides: {e.Message}");
                m_Model = new FileModel();
            }
        }

        void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(m_Path));
            File.WriteAllText(m_Path, JsonUtility.ToJson(m_Model, true));
        }
    }
}
