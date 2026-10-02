using System;
using System.Collections.Generic;
using System.Linq;
using RoadReady.Config;
using RoadReady.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoadReady.UI
{
    /// <summary>
    /// Researcher / administrator panel (use cases "Configure scenario parameters" and "Export session data").
    /// PIN-protected with an on-panel keypad because typing in VR is impractical.
    /// </summary>
    public class AdminScreen : UIScreen
    {
        string m_Pin = "";
        ScenarioDefinition m_Scenario;
        int m_Level = 1;
        ScenarioParameters m_Editing;

        public override string TreeName => "Admin";

        protected override void Bind()
        {
            Btn("close", App.GoToSetup);
            BuildKeypad();
            Btn("tab-scenarios", () => ShowTab(true));
            Btn("tab-data", () => ShowTab(false));
            Btn("save", SaveParameters);
            Btn("reset", ResetParameters);
            Btn("export", Export);
            Btn("delete-all", ConfirmDeleteAll);
        }

        // ------------------------------------------------------------ PIN

        void BuildKeypad()
        {
            var keypad = Q<VisualElement>("keypad");
            foreach (var key in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "C", "0", "OK" })
            {
                var k = key;
                keypad.Add(UIX.MakeButton(k, () => OnKey(k), k == "OK" ? "btn--primary" : "btn--ghost"));
            }

            RenderPin();
        }

        void OnKey(string key)
        {
            if (key == "C")
            {
                m_Pin = "";
            }
            else if (key == "OK")
            {
                if (m_Pin == App.Config.adminPin)
                {
                    Q<VisualElement>("pin-view").SetVisible(false);
                    Q<VisualElement>("admin-view").SetVisible(true);
                    ShowTab(true);
                    return;
                }

                m_Pin = "";
                Core.RoadReadyEvents.Toast("Wrong PIN");
            }
            else if (m_Pin.Length < 8)
            {
                m_Pin += key;
            }

            RenderPin();
        }

        void RenderPin() => Text("pin", m_Pin.Length == 0 ? "-" : new string('*', m_Pin.Length));

        void ShowTab(bool scenarios)
        {
            Q<VisualElement>("scenarios-panel").SetVisible(scenarios);
            Q<VisualElement>("data-panel").SetVisible(!scenarios);
            Q<Button>("tab-scenarios").EnableInClassList("chip--selected", scenarios);
            Q<Button>("tab-data").EnableInClassList("chip--selected", !scenarios);
            if (scenarios)
                BuildScenarioChips();
            else
                RefreshData();
        }

        // ------------------------------------------------------------ scenario parameters (FR11)

        List<ScenarioDefinition> AllScenarios()
        {
            var list = new List<ScenarioDefinition>();
            if (App.Config.driverTutorial != null) list.Add(App.Config.driverTutorial);
            if (App.Config.pedestrianTutorial != null && App.Config.pedestrianTutorial != App.Config.driverTutorial) list.Add(App.Config.pedestrianTutorial);
            list.AddRange(App.Config.scenarios.Where(s => s != null));
            return list;
        }

        void BuildScenarioChips()
        {
            var scenarios = AllScenarios();
            m_Scenario ??= scenarios.FirstOrDefault();
            UIX.BuildChips(Q<VisualElement>("scenario-chips"), scenarios, s => s.displayName, m_Scenario, s =>
            {
                m_Scenario = s;
                m_Level = 1;
                BuildLevelChips();
            });
            BuildLevelChips();
        }

        void BuildLevelChips()
        {
            if (m_Scenario == null)
                return;
            var levels = Enumerable.Range(1, m_Scenario.LevelCount).ToList();
            UIX.BuildChips(Q<VisualElement>("level-chips"), levels, l => $"Level {l}", m_Level, l =>
            {
                m_Level = l;
                BuildParameters();
            });
            BuildParameters();
        }

        void BuildParameters()
        {
            var container = Q<ScrollView>("params");
            container.Clear();
            if (m_Scenario == null)
                return;
            m_Editing = App.Overrides.Resolve(m_Scenario, m_Level);
            Text("override-state", App.Overrides.HasOverride(m_Scenario, m_Level) ? "Using saved overrides for this level." : "Using the shipped defaults for this level.");
            var p = m_Editing;

            container.Add(UIX.MakeLabel("Traffic", "h2"));
            AddSlider(container, "Vehicles per minute", 0f, 40f, p.trafficDensityPerMinute, v => p.trafficDensityPerMinute = v, "0.0");
            AddSlider(container, "Moto-taxi share", 0f, 1f, p.motoTaxiShare, v => p.motoTaxiShare = v, "0%");
            AddSlider(container, "Bus share", 0f, 1f, p.busShare, v => p.busShare = v, "0%");
            AddSlider(container, "Bicycle share", 0f, 1f, p.bicycleShare, v => p.bicycleShare = v, "0%");
            AddSlider(container, "NPC cruise speed (km/h)", 10f, 80f, p.npcCruiseSpeedKmh, v => p.npcCruiseSpeedKmh = v, "0");
            AddSlider(container, "Drivers yielding at zebra", 0f, 1f, p.driverYieldCompliance, v => p.driverYieldCompliance = v, "0%");
            AddSlider(container, "Drivers obeying signals", 0f, 1f, p.signalCompliance, v => p.signalCompliance = v, "0%");
            AddSlider(container, "Pedestrians per minute", 0f, 30f, p.pedestrianDensityPerMinute, v => p.pedestrianDensityPerMinute = v, "0.0");
            AddSlider(container, "Pedestrian jaywalk rate", 0f, 1f, p.pedestrianJaywalkRate, v => p.pedestrianJaywalkRate = v, "0%");

            container.Add(UIX.MakeLabel("Rules and timing", "h2"));
            AddSlider(container, "Speed limit (km/h)", 20f, 80f, p.speedLimitKmh, v => p.speedLimitKmh = Mathf.Round(v / 5f) * 5f, "0");
            AddSlider(container, "Signal cycle scale", 0.5f, 2f, p.signalCycleScale, v => p.signalCycleScale = v, "0.00x");
            AddSlider(container, "Time limit (s)", 30f, 600f, p.timeLimitSeconds, v => p.timeLimitSeconds = Mathf.Round(v / 10f) * 10f, "0");
            AddSlider(container, "Hazard warning time", 0.5f, 1.5f, p.hazardLeadTimeScale, v => p.hazardLeadTimeScale = v, "0.00x");
            AddSlider(container, "Random seed", 1f, 9999f, p.randomSeed, v => p.randomSeed = Mathf.RoundToInt(v), "0");
            AddToggle(container, "End attempt on collision", p.endAttemptOnCollision, v => p.endAttemptOnCollision = v);

            container.Add(UIX.MakeLabel("Environment", "h2"));
            var tod = new VisualElement();
            tod.AddToClassList("row");
            UIX.BuildEnumChips(tod, p.timeOfDay, t => t.ToString(), v => p.timeOfDay = v);
            container.Add(tod);
            var weather = new VisualElement();
            weather.AddToClassList("row");
            UIX.BuildEnumChips(weather, p.weather, w => w.ToString(), v => p.weather = v);
            container.Add(weather);

            if (m_Scenario.hazards.Count > 0)
            {
                container.Add(UIX.MakeLabel("Hazards", "h2"));
                foreach (var hazard in m_Scenario.hazards.Where(h => h != null))
                {
                    var id = hazard.hazardId;
                    AddToggle(container, $"{hazard.displayName} ({hazard.perspective})", p.IsHazardEnabled(id), enabled =>
                    {
                        p.disabledHazardIds.Remove(id);
                        if (!enabled) p.disabledHazardIds.Add(id);
                    });
                }
            }
        }

        static void AddSlider(VisualElement container, string label, float min, float max, float value, Action<float> set, string format)
        {
            var slider = new Slider(label, min, max) { value = value };
            slider.label = $"{label}: {value.ToString(format)}";
            slider.RegisterValueChangedCallback(e =>
            {
                set(e.newValue);
                slider.label = $"{label}: {e.newValue.ToString(format)}";
            });
            container.Add(slider);
        }

        static void AddToggle(VisualElement container, string label, bool value, Action<bool> set)
        {
            var toggle = new Toggle { text = label, value = value };
            toggle.RegisterValueChangedCallback(e => set(e.newValue));
            container.Add(toggle);
        }

        void SaveParameters()
        {
            if (m_Scenario == null || m_Editing == null)
                return;
            App.Overrides.Set(m_Scenario, m_Level, m_Editing);
            Core.RoadReadyEvents.Toast("Parameters saved");
            BuildParameters();
        }

        void ResetParameters()
        {
            if (m_Scenario == null)
                return;
            App.Overrides.Clear(m_Scenario, m_Level);
            BuildParameters();
        }

        // ------------------------------------------------------------ data

        void RefreshData()
        {
            var participants = App.Data.LoadAllParticipants();
            var attempts = participants.Sum(p => App.Data.GetAttempts(p.participantId).Count);
            Text("data-stats", $"{participants.Count} participant(s), {attempts} attempt(s) stored on this headset.\nData folder: {App.Data.RootFolder}");
            var remote = App.Remote;
            Text("sync-state", remote == null || !remote.Enabled
                ? "Cloud backup: off (set a backend in RoadReadyConfig)."
                : $"Cloud backup: {remote.PendingCount} record(s) waiting to upload.{(remote.LastError != null ? " Last error: " + remote.LastError : "")}");
        }

        void Export()
        {
            try
            {
                var folder = CsvExporter.Export(App.Data);
                Text("export-path", "Exported to: " + folder + "\nOn Quest, copy with: adb pull " + folder);
                Debug.Log("[RoadReady] CSV export written to " + folder);
            }
            catch (Exception e)
            {
                Text("export-path", "Export failed: " + e.Message);
            }
        }

        void ConfirmDeleteAll()
        {
            UI.Confirm("Delete ALL study data?", "Every participant, session, attempt and telemetry file on this headset will be permanently deleted. Export first if you need the data.",
                "Delete everything", () =>
                {
                    foreach (var id in App.Data.ListParticipantIds())
                        App.Data.DeleteParticipant(id);
                    RefreshData();
                });
        }
    }
}
