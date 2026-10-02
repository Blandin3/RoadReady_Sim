using System.Collections.Generic;
using System.Linq;
using RoadReady.Config;
using RoadReady.Core;
using RoadReady.Data;
using RoadReady.Player;
using RoadReady.Scenarios;
using RoadReady.UI;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.UI;
using static RoadReady.EditorTools.GreyboxKit;

namespace RoadReady.EditorTools
{
    /// <summary>One-click project setup: RoadReady > Build Everything.</summary>
    public static class RoadReadySetup
    {
        const string UiFolder = Root + "/UI";
        const string BootstrapScene = ScenarioSceneBuilder.ScenesFolder + "/RR_Bootstrap.unity";
        const string XrOriginGuid = "f6336ac4ac8b4d34bc5072418cdc62a0";
        const string VignetteGuid = "6c8af5c8012f01440af6cb2bc3eb987c";

        static readonly string[] k_Screens =
        {
            "ParticipantSetup", "Consent", "Message", "Instructions", "MainMenu", "Briefing", "Feedback",
            "Questionnaire", "Comfort", "Pause", "Debrief", "Admin", "SafetyBriefing",
        };

        [MenuItem("RoadReady/Build Everything", priority = 0)]
        public static void BuildEverything()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            try
            {
                PrepareAssets();
                BuildScenarioScenes();
                BuildBootstrap();
                Finish();
                Debug.Log("[RoadReady] Build complete. Press Play (it always starts from RR_Bootstrap). Use the XR Interaction Simulator or a Quest via Link.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("RoadReady/Rebuild Scenario Scenes Only", priority = 20)]
        public static void RebuildScenes()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            try
            {
                PrepareAssets();
                BuildScenarioScenes();
                Finish();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// All asset creation happens here, followed by one synchronous import. Scene building afterwards only
        /// loads assets by path, so no in-memory reference can be invalidated by a mid-build import.
        /// </summary>
        static void PrepareAssets()
        {
            EditorUtility.DisplayProgressBar("RoadReady", "Creating materials and prefabs...", 0.1f);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            PrewarmSharedAssets();
            EditorUtility.DisplayProgressBar("RoadReady", "Creating configuration assets...", 0.25f);
            WorldPanelSettings();
            BuildConfigAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        static void Finish()
        {
            ApplyBuildSettings();
            AssetDatabase.SaveAssets();
            // Scenario scenes deliberately contain no camera (the XR rig lives in the bootstrap scene), so
            // pressing Play in one of them would show "No cameras rendering". Always enter Play Mode via bootstrap.
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapScene);
            EditorSceneManager.OpenScene(BootstrapScene);
        }

        [MenuItem("RoadReady/Play From Current Scene (disable bootstrap redirect)", priority = 60)]
        public static void ClearPlayModeStartScene() => EditorSceneManager.playModeStartScene = null;

        [InitializeOnLoadMethod]
        static void RestorePlayModeStartScene()
        {
            // playModeStartScene is not persisted across editor restarts; restore it once the bootstrap exists.
            EditorApplication.delayCall += () =>
            {
                if (EditorSceneManager.playModeStartScene == null)
                {
                    var bootstrap = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapScene);
                    if (bootstrap != null)
                        EditorSceneManager.playModeStartScene = bootstrap;
                }
            };
        }

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new System.InvalidOperationException($"[RoadReady] Expected asset missing at {path}.");
            return asset;
        }

        [MenuItem("RoadReady/Open Data Folder", priority = 40)]
        public static void OpenDataFolder()
        {
            var folder = System.IO.Path.Combine(Application.persistentDataPath, "RoadReady");
            System.IO.Directory.CreateDirectory(folder);
            EditorUtility.RevealInFinder(folder);
        }

        [MenuItem("RoadReady/Export CSV (editor data)", priority = 41)]
        public static void ExportCsv()
        {
            var folder = CsvExporter.Export(new DataService());
            EditorUtility.RevealInFinder(folder);
        }

        // ================================================================ config assets

        static void BuildConfigAssets()
        {
            EnsureFolder(HazardFolder);
            EnsureFolder(ScenarioFolder);
            var scoring = LoadOrCreate<ScoringConfig>(ConfigFolder + "/ScoringConfig.asset");

            var hazards = new Dictionary<string, HazardDefinition>();
            void H(string id, string name, HazardType type, Perspective p, RequiredResponse r, float window, float safe, float border, int severity,
                string ok, string late, string bad, string insight)
            {
                var h = LoadOrCreate<HazardDefinition>($"{HazardFolder}/{id}.asset");
                h.hazardId = id;
                h.displayName = name;
                h.type = type;
                h.perspective = p;
                h.requiredResponse = r;
                h.responseWindowSeconds = window;
                h.safeReactionSeconds = safe;
                h.borderlineReactionSeconds = border;
                h.severity = severity;
                h.feedbackSafe = ok;
                h.feedbackBorderline = late;
                h.feedbackUnsafe = bad;
                h.otherPerspectiveInsight = insight;
                EditorUtility.SetDirty(h);
                hazards[id] = h;
            }

            // Zebra crossing
            H("zebra_ped_steps_out", "Pedestrian steps onto the zebra", HazardType.PedestrianStepsOut, Perspective.Driver, RequiredResponse.StopBeforePoint, 6f, 1.5f, 2.5f, 3,
                "You slowed early and stopped - the pedestrian could cross safely.",
                "You stopped, but late. Start slowing as soon as you see anyone near a zebra.",
                "You did not stop for the pedestrian on the zebra crossing.",
                "From the kerb, the pedestrian could not tell whether you would stop. Slowing early is how a driver says 'go ahead'.");
            H("zebra_jaywalker", "Pedestrian darts across the road", HazardType.JaywalkingPedestrian, Perspective.Driver, RequiredResponse.Brake, 4f, 1.0f, 1.8f, 3,
                "Quick reaction - you braked as soon as the pedestrian moved.",
                "You braked, but a little late. Watch people on the pavement, not just the road.",
                "You did not brake for the pedestrian crossing in front of you.",
                "Pedestrians far from a crossing often misjudge how fast cars approach - to them you looked far away.");
            H("zebra_car_no_yield", "Car does not stop at the zebra", HazardType.VehicleFailsToYield, Perspective.Pedestrian, RequiredResponse.WaitAtKerb, 7f, 1.5f, 2.5f, 3,
                "You checked, saw the car was not slowing and waited. Exactly right.",
                "You waited, but did not clearly look at the car first. Check every lane before deciding.",
                "You stepped out in front of a car that was not stopping. A zebra gives you priority only once drivers have actually stopped.",
                "From the driver's seat, a pedestrian standing back from the kerb is easy to miss at speed. Step to the edge, make eye contact and wait until the car has clearly stopped.");
            H("zebra_moto_far_lane", "Moto-taxi in the far lane", HazardType.ObscuredVehicle, Perspective.Pedestrian, RequiredResponse.StopOrRetreat, 5f, 1.2f, 2.2f, 3,
                "You checked the second lane and held back. Well done.",
                "You reacted, but late. Pause at the centre line and look right before the second lane.",
                "You walked into the path of the moto-taxi in the far lane.",
                "A rider in the far lane cannot see a pedestrian stepping out from behind a stopped car. Pause at the centre line and check the second lane.");
            // Signalised intersection
            H("signal_red_runner", "Car runs the red light", HazardType.RedLightRunner, Perspective.Driver, RequiredResponse.Brake, 6f, 1.2f, 2.0f, 3,
                "You checked the junction before moving off and avoided the red-light runner.",
                "You avoided it, but only just. Scan left and right before you go on green.",
                "You drove into the path of a car running the red light.",
                "A green light means you may go - not that it is safe. The other driver was racing the amber and was not looking for you.");
            H("signal_late_pedestrian", "Pedestrian still crossing on your green", HazardType.PedestrianOnCrossing, Perspective.Driver, RequiredResponse.StopBeforePoint, 7f, 1.5f, 2.5f, 3,
                "You gave the slow pedestrian time to finish crossing.",
                "You stopped late for the pedestrian on the crossing.",
                "You did not wait for the pedestrian still on the crossing.",
                "The pedestrian started while the green figure flashed. From their side, the crossing is long and cars start fast.");
            H("signal_red_runner_ped", "Moto-taxi runs the red light", HazardType.RedLightRunner, Perspective.Pedestrian, RequiredResponse.WaitAtKerb, 5f, 1.2f, 2.0f, 3,
                "Even on green you looked first - and spotted the moto-taxi running the red.",
                "You waited, but did not clearly look first. Always check before stepping out, even on green.",
                "You stepped out on green in front of a moto-taxi running the red light.",
                "The rider was trying to beat the amber and was watching the light, not the crossing.");
            H("signal_turning_car", "Turning car cuts across the crossing", HazardType.TurningVehicle, Perspective.Pedestrian, RequiredResponse.StopOrRetreat, 6f, 1.5f, 2.5f, 3,
                "You saw the turning car and let it pass.",
                "You reacted late to the turning car.",
                "You were caught by a car turning across the crossing.",
                "Drivers turning left are busy looking for gaps in oncoming traffic and often notice pedestrians last.");
            // Moto-taxi junction
            H("junction_moto_sudden_stop", "Moto-taxi stops suddenly for a passenger", HazardType.MotoTaxiSuddenStop, Perspective.Driver, RequiredResponse.Brake, 4f, 1.0f, 1.8f, 2,
                "You kept your distance and braked in time.",
                "You braked late. Keep two seconds behind moto-taxis.",
                "You did not stop in time for the moto-taxi picking up a passenger.",
                "Moto-taxi riders stop wherever a passenger waves. Their customer is at the roadside - not behind them.");
            H("junction_hidden_moto", "Fast moto-taxi hidden behind a parked bus", HazardType.VehicleFromBlindSpot, Perspective.Driver, RequiredResponse.Brake, 5f, 1.5f, 2.5f, 3,
                "You waited and looked again - the hidden moto-taxi passed safely.",
                "You held back, but only just. Creep forward slowly and look again when your view is blocked.",
                "You pulled out in front of the moto-taxi hidden behind the bus.",
                "The rider could not see you behind the bus either. When your view is blocked, edge forward and look again.");
            H("junction_turning_car_ped", "Car turns into the side road without signalling", HazardType.TurningVehicle, Perspective.Pedestrian, RequiredResponse.WaitAtKerb, 7f, 1.5f, 2.5f, 3,
                "You watched the car's movement, not just its indicator, and waited.",
                "You waited, but without clearly checking the car. Watch the wheels and speed of turning cars.",
                "You stepped out as a car turned into the side road.",
                "The driver was watching main-road traffic and did not signal. Watch the wheels and the car's speed, not just the indicator.");
            H("junction_moto_exit", "Moto-taxi pulls out of the side road", HazardType.VehicleFromBlindSpot, Perspective.Pedestrian, RequiredResponse.StopOrRetreat, 5f, 1.2f, 2.2f, 3,
                "You spotted the moto-taxi pulling out and stepped back.",
                "You reacted late to the moto-taxi.",
                "You were in the path of the moto-taxi leaving the side road.",
                "A rider waiting to join the main road is looking right for a gap - not at pedestrians crossing in front.");

            ScenarioDefinition S(string file, string id, string name, ScenarioType type, string scene, string description, string driver, string pedestrian, params string[] hazardIds)
            {
                var s = LoadOrCreate<ScenarioDefinition>($"{ScenarioFolder}/{file}.asset");
                s.scenarioId = id;
                s.displayName = name;
                s.type = type;
                s.sceneName = scene;
                s.location = "Kimironko, Kigali";
                s.description = description;
                s.driverBriefing = driver;
                s.pedestrianBriefing = pedestrian;
                s.isTutorial = type == ScenarioType.Tutorial;
                s.supportedPerspectives = PerspectiveMask.Both;
                s.hazards = hazardIds.Select(h => hazards[h]).ToList();
                EditorUtility.SetDirty(s);
                return s;
            }

            var tutorial = S("Scenario_Tutorial", "tutorial", "Tutorial", ScenarioType.Tutorial, "RR_Tutorial",
                "Learn the controls on a quiet road. Not scored.", "Learn to accelerate, brake, steer, indicate and check your blind spot.",
                "Learn to look, walk and cross.");
            tutorial.location = "Practice road";
            var zebra = S("Scenario_Zebra", "zebra_crossing", "Market zebra crossing", ScenarioType.UnsignalizedZebraCrossing, "RR_ZebraCrossing",
                "An unsignalised zebra crossing outside Kimironko market with busy foot traffic.",
                "Drive east along the market road through the 30 km/h zone and past the zebra crossing. Watch for people on and around the crossing.",
                "Walk to the zebra crossing and cross to the other side of the road. Cars do not always stop.",
                "zebra_ped_steps_out", "zebra_jaywalker", "zebra_car_no_yield", "zebra_moto_far_lane");
            var signal = S("Scenario_Signal", "signalized_intersection", "Signalised intersection", ScenarioType.SignalizedIntersection, "RR_SignalizedIntersection",
                "A four-way signalised junction with cars, buses, moto-taxis and pedestrians.",
                "Drive straight through the traffic lights. Obey the signals and yield to anyone still on the crossings.",
                "Cross the road at the signalised crossing on the west side of the junction.",
                "signal_red_runner", "signal_late_pedestrian", "signal_red_runner_ped", "signal_turning_car");
            var junction = S("Scenario_Junction", "moto_taxi_junction", "Moto-taxi junction", ScenarioType.UnsignalizedJunction, "RR_MotoTaxiJunction",
                "An unsignalised T-junction where moto-taxis pick up passengers and the view is partly blocked.",
                "Drive up the side road and turn LEFT onto the main road. Give way to main-road traffic and signal your turn.",
                "Walk along the pavement and cross the mouth of the side road. There is no crossing - judge the traffic yourself.",
                "junction_moto_sudden_stop", "junction_hidden_moto", "junction_turning_car_ped", "junction_moto_exit");

            // Difficulty levels (admin-editable at runtime via overrides).
            ScenarioSceneBuilder.Params(tutorial, 600f, traffic: false);
            ScenarioSceneBuilder.Params(zebra, 150f);
            ScenarioSceneBuilder.Params(signal, 180f);
            ScenarioSceneBuilder.Params(junction, 180f);

            var config = LoadOrCreate<RoadReadyConfig>(ConfigPath);
            config.scoring = scoring;
            config.driverTutorial = tutorial;
            config.pedestrianTutorial = tutorial;
            config.scenarios = new List<ScenarioDefinition> { zebra, signal, junction };
            EditorUtility.SetDirty(config);
        }

        static void BuildScenarioScenes()
        {
            string P(string file) => $"{ScenarioFolder}/{file}.asset";
            EditorUtility.DisplayProgressBar("RoadReady", "Building tutorial scene...", 0.4f);
            ScenarioSceneBuilder.BuildTutorial(P("Scenario_Tutorial"));
            EditorUtility.DisplayProgressBar("RoadReady", "Building zebra crossing scene...", 0.5f);
            ScenarioSceneBuilder.BuildZebra(P("Scenario_Zebra"));
            EditorUtility.DisplayProgressBar("RoadReady", "Building signalised intersection scene...", 0.6f);
            ScenarioSceneBuilder.BuildSignalized(P("Scenario_Signal"));
            EditorUtility.DisplayProgressBar("RoadReady", "Building moto-taxi junction scene...", 0.7f);
            ScenarioSceneBuilder.BuildJunction(P("Scenario_Junction"));
            AssetDatabase.SaveAssets();
        }

        // ================================================================ bootstrap

        static PanelSettings WorldPanelSettings()
        {
            var path = ConfigFolder + "/RoadReadyWorldPanel.asset";
            var settings = LoadOrCreate<PanelSettings>(path);
            settings.renderMode = PanelRenderMode.WorldSpace;
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(UiFolder + "/Theme/RoadReadyTheme.tss");
            if (theme == null)
                Debug.LogWarning("[RoadReady] RoadReadyTheme.tss not imported yet - UI will use no theme.");
            settings.themeStyleSheet = theme;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        static VisualTreeAsset Tree(string name) => AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{UiFolder}/UXML/{name}.uxml");

        static WorldSpacePanel Panel(Transform parent, string name, string uxml, PanelSettings settings, Vector2 size, float metersPerPixel, bool interactive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            doc.visualTreeAsset = Tree(uxml);
            doc.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Fixed;
            doc.worldSpaceSize = size;
            var panel = go.AddComponent<WorldSpacePanel>();
            panel.Configure(size, metersPerPixel, interactive);
            go.transform.localScale = Vector3.one * metersPerPixel;
            return panel;
        }

        static void BuildBootstrap()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorUtility.DisplayProgressBar("RoadReady", "Building bootstrap scene...", 0.85f);
            var panelSettings = Load<PanelSettings>(ConfigFolder + "/RoadReadyWorldPanel.asset");
            var config = Load<RoadReadyConfig>(ConfigPath);

            // Lobby (hidden while a scenario is loaded).
            var lobby = new GameObject("Lobby").transform;
            var sun = new GameObject("Lobby Light");
            sun.transform.SetParent(lobby, false);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(50f, -20f, 0f);
            Box(lobby, "Floor", new Vector3(0f, -0.05f, 0f), new Vector3(20f, 0.1f, 20f), "lobby", collider: true);
            Box(lobby, "Stripe blue", new Vector3(0f, 2.5f, 6f), new Vector3(12f, 1.2f, 0.1f), "rw_blue");
            Box(lobby, "Stripe yellow", new Vector3(0f, 1.6f, 6f), new Vector3(12f, 0.6f, 0.1f), "rw_yellow");
            Box(lobby, "Stripe green", new Vector3(0f, 1.0f, 6f), new Vector3(12f, 0.6f, 0.1f), "rw_green");
            var lobbySpawn = Point(lobby, "Lobby Spawn", Vector3.zero);

            // XR rig from XRI Starter Assets.
            var originPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(XrOriginGuid));
            if (originPrefab == null)
            {
                Debug.LogError("[RoadReady] XR Origin (XR Rig) prefab not found. Import XRI 'Starter Assets' sample (3.5.1).");
                return;
            }

            var originGo = (GameObject)PrefabUtility.InstantiatePrefab(originPrefab);
            var origin = originGo.GetComponent<XROrigin>();
            var camera = origin.Camera;
            var vignettePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(VignetteGuid));
            if (vignettePrefab != null)
                PrefabUtility.InstantiatePrefab(vignettePrefab, camera.transform);

            var gaze = originGo.AddComponent<GazeTracker>();
            gaze.Head = camera.transform;
            var pedestrian = originGo.AddComponent<PedestrianController>();
            pedestrian.enabled = false;
            var vignette = originGo.AddComponent<MotionComfortVignette>();

            new GameObject("XR UI Toolkit Manager").AddComponent<XRUIToolkitManager>();

            // RoadReady systems.
            var app = new GameObject("RoadReady");
            var rig = app.AddComponent<PlayerRig>();
            SetRef(rig, "m_Origin", origin);
            rig.LobbySpawn = lobbySpawn;
            var scenarios = app.AddComponent<ScenarioManager>();
            var remote = app.AddComponent<RemoteSync>();
            app.AddComponent<AudioSource>();
            var voice = app.AddComponent<InstructionVoice>();
            var ui = app.AddComponent<RoadReadyUI>();
            var appComponent = app.AddComponent<RoadReadyApp>();

            var menu = Panel(null, "Menu Panel", "MenuShell", panelSettings, new Vector2(1600f, 1000f), 0.001f, true);
            menu.transform.position = new Vector3(0f, 1.4f, 1.7f);
            var hud = Panel(null, "HUD Panel", "HUD", panelSettings, new Vector2(1300f, 800f), 0.001f, false);
            var hudController = hud.gameObject.AddComponent<HudController>();
            var fader = Panel(camera.transform, "Screen Fader", "Fader", panelSettings, new Vector2(1600f, 1600f), 0.0005f, false);
            fader.transform.localPosition = new Vector3(0f, 0f, 0.15f);
            var faderComponent = fader.gameObject.AddComponent<ScreenFader>();

            SetRef(scenarios, "m_Rig", rig);
            SetRef(scenarios, "m_Gaze", gaze);
            SetRef(scenarios, "m_Pedestrian", pedestrian);
            SetRef(scenarios, "m_Vignette", vignette);
            SetRef(scenarios, "m_Lobby", lobby.gameObject);

            SetRef(ui, "m_MenuPanel", menu);
            var uiSo = new SerializedObject(ui);
            var screens = uiSo.FindProperty("m_Screens");
            screens.arraySize = k_Screens.Length;
            for (var i = 0; i < k_Screens.Length; i++)
            {
                var element = screens.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("name").stringValue = k_Screens[i];
                element.FindPropertyRelative("tree").objectReferenceValue = Tree(k_Screens[i]);
            }

            uiSo.ApplyModifiedPropertiesWithoutUndo();

            SetRef(appComponent, "m_Config", config);
            SetRef(appComponent, "m_Scenarios", scenarios);
            SetRef(appComponent, "m_UI", ui);
            SetRef(appComponent, "m_Hud", hudController);
            SetRef(appComponent, "m_Fader", faderComponent);
            SetRef(appComponent, "m_Rig", rig);
            SetRef(appComponent, "m_Pedestrian", pedestrian);
            SetRef(appComponent, "m_Vignette", vignette);
            SetRef(appComponent, "m_Voice", voice);
            SetRef(appComponent, "m_Remote", remote);

            EnsureFolder(ScenarioSceneBuilder.ScenesFolder);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), BootstrapScene);
        }

        static void ApplyBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(BootstrapScene, true) };
            var config = Load<RoadReadyConfig>(ConfigPath);
            var names = new List<string> { config.driverTutorial.sceneName };
            names.AddRange(config.scenarios.Select(s => s.sceneName));
            foreach (var n in names.Distinct())
                scenes.Add(new EditorBuildSettingsScene($"{ScenarioSceneBuilder.ScenesFolder}/{n}.unity", true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
