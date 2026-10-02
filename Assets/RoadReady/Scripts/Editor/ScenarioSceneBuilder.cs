using System.Collections.Generic;
using RoadReady.Config;
using RoadReady.Core;
using RoadReady.Hazards;
using RoadReady.Player;
using RoadReady.Scenarios;
using RoadReady.Traffic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using static RoadReady.EditorTools.GreyboxKit;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// Builds greybox versions of the proposal's scenario set (section 1.5). Coordinates: +x east, +z north,
    /// right-hand traffic (eastbound lane z = -1.75, westbound z = +1.75, northbound x = +1.75, southbound x = -1.75).
    /// </summary>
    public static class ScenarioSceneBuilder
    {
        public const string ScenesFolder = GeneratedRoot + "/Scenes";
        const float HalfRoad = 3.5f;
        const float Lane = 1.75f;

        class Rig
        {
            public Transform root, env, zones, rules, paths, traffic, actors, hazards, spawns;
            public ScenarioContext ctx;
        }

        // ================================================================ common

        /// <summary>
        /// Loads a hazard definition by id at the moment it is assigned. Never cache asset references across
        /// scene changes / imports - the in-memory object can be replaced and the old reference then reads as destroyed.
        /// </summary>
        static HazardDefinition HZ(string id)
        {
            var hazard = AssetDatabase.LoadAssetAtPath<HazardDefinition>($"{HazardFolder}/{id}.asset");
            if (hazard == null)
                throw new System.InvalidOperationException($"[RoadReady] Hazard asset '{id}' is missing. Run RoadReady > Build Everything.");
            return hazard;
        }

        static Rig NewScene(string sceneName, string definitionPath)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var definition = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(definitionPath);
            if (definition == null)
                throw new System.InvalidOperationException($"[RoadReady] Scenario asset missing at {definitionPath}.");
            var sun = new GameObject("Sun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(52f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.85f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.52f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.22f, 0.18f);
            RenderSettings.sun = light;

            var root = new GameObject(sceneName).transform;
            var rig = new Rig
            {
                root = root,
                env = Group(root, "Environment (greybox - replace with final art)"),
                zones = Group(root, "Road Zones"),
                rules = Group(root, "Rules"),
                paths = Group(root, "Paths"),
                traffic = Group(root, "Traffic"),
                actors = Group(root, "Scripted Actors"),
                hazards = Group(root, "Hazards"),
                spawns = Group(root, "Spawns"),
            };
            rig.ctx = root.gameObject.AddComponent<ScenarioContext>();
            rig.ctx.Definition = definition;
            Ground(rig.env);
            return rig;
        }

        static void Save(Rig rig, string sceneName)
        {
            EnsureFolder(ScenesFolder);
            EditorUtility.SetDirty(rig.ctx);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), $"{ScenesFolder}/{sceneName}.unity");
        }

        static void PlayerCar(Rig rig, Vector3 position, float yaw)
        {
            var spawn = Point(rig.spawns, "Driver Spawn", position, yaw);
            var car = (GameObject)PrefabUtility.InstantiatePrefab(PlayerCarPrefab().gameObject, rig.root);
            car.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            rig.ctx.DriverSpawn = spawn;
            rig.ctx.PlayerVehicle = car.GetComponent<DriverController>();
        }

        static void PedestrianSpawn(Rig rig, Vector3 position, float yaw) =>
            rig.ctx.PedestrianSpawn = Point(rig.spawns, "Pedestrian Spawn", position, yaw);

        static void Goal(Rig rig, string name, Vector3 center, Vector3 size, PerspectiveMask perspectives, bool requiresCrossing = true)
        {
            var goal = Point(rig.spawns, name, center).gameObject.AddComponent<GoalZone>();
            goal.Size = size;
            goal.Perspectives = perspectives;
            goal.RequiresCrossing = requiresCrossing;
            rig.ctx.Goals.Add(goal);
        }

        static TrafficSpawner Spawner(Rig rig, string name, WaypointPath path, float densityScale, int prewarm)
        {
            var spawner = Group(rig.traffic, name).gameObject.AddComponent<TrafficSpawner>();
            spawner.Path = path;
            spawner.DensityScale = densityScale;
            spawner.PrewarmCount = prewarm;
            foreach (var kind in new[] { RoadUserKind.Car, RoadUserKind.MotoTaxi, RoadUserKind.Bus, RoadUserKind.Bicycle })
                spawner.Prefabs.Add(new VehiclePrefabEntry { kind = kind, prefab = VehiclePrefab(kind) });
            rig.ctx.TrafficSpawners.Add(spawner);
            return spawner;
        }

        static PedestrianSpawner PedSpawner(Rig rig, WaypointPath[] paths, WaypointPath[] jaywalk)
        {
            var spawner = Group(rig.traffic, "Pedestrian Spawner").gameObject.AddComponent<PedestrianSpawner>();
            spawner.Paths.AddRange(paths);
            spawner.JaywalkPaths.AddRange(jaywalk);
            spawner.Prefabs.Add(PedestrianPrefab("A"));
            spawner.Prefabs.Add(PedestrianPrefab("B"));
            rig.ctx.PedestrianSpawners.Add(spawner);
            return spawner;
        }

        static TrafficVehicleAI ScriptedVehicle(Rig rig, RoadUserKind kind, string name, WaypointPath path, float startDistance, float cruiseKmh,
            bool dormant, bool hidden, Perspective activeFor, bool obeys = true, bool yields = true)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(VehiclePrefab(kind).gameObject, rig.actors);
            go.name = name;
            var ai = go.GetComponent<TrafficVehicleAI>();
            ai.SetDormant(dormant, hidden);
            ai.SetScripted(true);
            ai.Configure(path, startDistance, cruiseKmh, obeys, yields);
            (activeFor == Perspective.Driver ? rig.ctx.ActiveWhenDriving : rig.ctx.ActiveWhenWalking).Add(go);
            return ai;
        }

        static PedestrianAI ScriptedPedestrian(Rig rig, string name, WaypointPath path, Perspective activeFor, string variant = "A")
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(PedestrianPrefab(variant).gameObject, rig.actors);
            go.name = name;
            var ped = go.GetComponent<PedestrianAI>();
            ped.SetDormant(true, false);
            ped.SetScripted(true);
            ped.Configure(path, 0f, 1.3f, false, true);
            (activeFor == Perspective.Driver ? rig.ctx.ActiveWhenDriving : rig.ctx.ActiveWhenWalking).Add(go);
            return ped;
        }

        static HazardTrigger Hazard(Rig rig, HazardDefinition definition, HazardTriggerMode mode, Vector3 position)
        {
            var trigger = Point(rig.hazards, "Hazard - " + definition.displayName, position).gameObject.AddComponent<HazardTrigger>();
            trigger.Definition = definition;
            trigger.ActiveFor = definition.perspective.ToMask();
            trigger.Mode = mode;
            return trigger;
        }

        static void Act(HazardTrigger trigger, Component actor, HazardActorMode mode, float value = 0f, float delay = 0f) =>
            trigger.Actions.Add(new HazardAction { actor = actor, mode = mode, value = value, delay = delay });

        /// <summary>Straight E-W main road with sidewalks, centre line and optional side-road gap on the south side.</summary>
        static void MainRoad(Rig rig, float length, bool southGapForSideRoad = false, bool northGapForSideRoad = false)
        {
            var h = length * 0.5f;
            RoadRect(rig.env, "Main road", -h, h, -HalfRoad, HalfRoad);
            Zone(rig.zones, "Main carriageway", RoadZoneType.Carriageway, -h, h, -HalfRoad, HalfRoad);
            if (southGapForSideRoad)
            {
                SidewalkRect(rig.env, rig.zones, "Sidewalk S-W", -h, -HalfRoad, -6.5f, -HalfRoad);
                SidewalkRect(rig.env, rig.zones, "Sidewalk S-E", HalfRoad, h, -6.5f, -HalfRoad);
            }
            else
            {
                SidewalkRect(rig.env, rig.zones, "Sidewalk S", -h, h, -6.5f, -HalfRoad);
            }

            if (northGapForSideRoad)
            {
                SidewalkRect(rig.env, rig.zones, "Sidewalk N-W", -h, -HalfRoad, HalfRoad, 6.5f);
                SidewalkRect(rig.env, rig.zones, "Sidewalk N-E", HalfRoad, h, HalfRoad, 6.5f);
            }
            else
            {
                SidewalkRect(rig.env, rig.zones, "Sidewalk N", -h, h, HalfRoad, 6.5f);
            }

            var gap = southGapForSideRoad || northGapForSideRoad ? new[] { new Vector2(-8f, 8f) } : new Vector2[0];
            Buildings(rig.env, -h, h, 6.8f, +1, 11, northGapForSideRoad ? gap : new Vector2[0]);
            Buildings(rig.env, -h, h, -6.8f, -1, 17, southGapForSideRoad ? gap : new Vector2[0]);
            for (var x = -h + 20f; x < h; x += 35f)
            {
                if (Mathf.Abs(x) < 15f) continue;
                Tree(rig.env, new Vector3(x, SidewalkTop, -5.8f));
                Tree(rig.env, new Vector3(x + 17f, SidewalkTop, 5.8f));
            }
        }

        static (WaypointPath eb, WaypointPath wb) MainLanes(Rig rig, float length)
        {
            var h = length * 0.5f;
            return (Path(rig.paths, "Lane EB", new Vector3(-h, 0f, -Lane), new Vector3(h, 0f, -Lane)),
                Path(rig.paths, "Lane WB", new Vector3(h, 0f, Lane), new Vector3(-h, 0f, Lane)));
        }

        public static void Params(ScenarioDefinition def, float time, bool traffic = true)
        {
            def.difficultyLevels.Clear();
            for (var level = 1; level <= (traffic ? 3 : 1); level++)
            {
                def.difficultyLevels.Add(new ScenarioParameters
                {
                    randomSeed = 1000 + level,
                    trafficDensityPerMinute = traffic ? 6f + 6f * (level - 1) : 0f,
                    motoTaxiShare = 0.35f + 0.07f * (level - 1),
                    busShare = 0.1f,
                    bicycleShare = 0.05f,
                    npcCruiseSpeedKmh = 35f + 5f * (level - 1),
                    driverYieldCompliance = 0.6f - 0.15f * (level - 1),
                    signalCompliance = 0.95f - 0.05f * (level - 1),
                    pedestrianDensityPerMinute = traffic ? 5f + 3f * (level - 1) : 0f,
                    pedestrianJaywalkRate = 0.1f + 0.1f * (level - 1),
                    speedLimitKmh = 40f,
                    signalCycleScale = 1f,
                    timeLimitSeconds = time,
                    endAttemptOnCollision = true,
                    hazardLeadTimeScale = 1f - 0.15f * (level - 1),
                    timeOfDay = level == 3 ? TimeOfDay.Dusk : TimeOfDay.Day,
                });
            }

            EditorUtility.SetDirty(def);
        }

        // ================================================================ tutorial

        public static void BuildTutorial(string defPath)
        {
            const string name = "RR_Tutorial";
            var rig = NewScene(name, defPath);
            MainRoad(rig, 400f);
            CentreLineX(rig.env, -200f, 200f, 0f, 58f, 62f);
            ZebraAcrossEW(rig.env, rig.zones, "Tutorial zebra", 60f, HalfRoad, 4f);
            PlayerCar(rig, new Vector3(-150f, 0f, -Lane), 90f);
            PedestrianSpawn(rig, new Vector3(54f, SidewalkTop, -5.3f), 90f);
            var director = Group(rig.root, "Tutorial Director").gameObject.AddComponent<TutorialDirector>();
            director.KerbMarker = Point(rig.spawns, "Kerb marker", new Vector3(60f, 0f, -4.2f));
            Box(director.KerbMarker, "Marker disc", new Vector3(0f, SidewalkTop + 0.01f, 0f), new Vector3(0.9f, 0.02f, 0.9f), "marker");
            Save(rig, name);
        }

        // ================================================================ A: unsignalised zebra crossing

        public static void BuildZebra(string defPath)
        {
            const string name = "RR_ZebraCrossing";
            var rig = NewScene(name, defPath);
            MainRoad(rig, 320f);
            CentreLineX(rig.env, -160f, 160f, 0f, -3f, 3f);
            var zebra = ZebraAcrossEW(rig.env, rig.zones, "Zebra crossing", 0f, HalfRoad, 4f);
            var (eb, wb) = MainLanes(rig, 320f);

            var ebLine = StopLine(rig.rules, rig.env, "Zebra yield EB", new Vector3(-5.5f, 0f, -Lane), Vector3.right, StopLineType.ZebraYield, crossing: zebra);
            StopLine(rig.rules, rig.env, "Zebra yield WB", new Vector3(5.5f, 0f, Lane), Vector3.left, StopLineType.ZebraYield, crossing: zebra);
            var slow = Point(rig.rules, "Market speed zone 30", Vector3.zero).gameObject.AddComponent<SpeedLimitZone>();
            slow.Size = new Vector3(80f, 3f, 7f);
            slow.LimitKmh = 30f;

            // Background life.
            Spawner(rig, "Traffic EB", eb, 0.6f, 0);
            Spawner(rig, "Traffic WB", wb, 1f, 3);
            var s2n = Path(rig.paths, "Ped S->N via zebra", new Vector3(-70f, 0f, -5.2f), new Vector3(-0.8f, 0f, -5.2f), new Vector3(-0.8f, 0f, 5.2f), new Vector3(70f, 0f, 5.2f));
            var n2s = Path(rig.paths, "Ped N->S via zebra", new Vector3(70f, 0f, 5.8f), new Vector3(0.8f, 0f, 5.8f), new Vector3(0.8f, 0f, -5.8f), new Vector3(-70f, 0f, -5.8f));
            var walkS = Path(rig.paths, "Ped walk S", new Vector3(-90f, 0f, -5.6f), new Vector3(90f, 0f, -5.6f));
            var walkN = Path(rig.paths, "Ped walk N", new Vector3(90f, 0f, 5.4f), new Vector3(-90f, 0f, 5.4f));
            var jay = Path(rig.paths, "Ped jaywalk", new Vector3(20f, 0f, -5.4f), new Vector3(45f, 0f, -5.4f), new Vector3(45f, 0f, 5.4f), new Vector3(80f, 0f, 5.4f));
            PedSpawner(rig, new[] { s2n, n2s, walkS, walkN }, new[] { jay });

            PlayerCar(rig, new Vector3(-110f, 0f, -Lane), 90f);
            Goal(rig, "Driver goal", new Vector3(70f, 0f, -Lane), new Vector3(6f, 3f, 7f), PerspectiveMask.Driver, false);
            PedestrianSpawn(rig, new Vector3(-4.5f, SidewalkTop, -5.2f), 90f);
            Goal(rig, "Pedestrian goal", new Vector3(0f, 0f, 6.2f), new Vector3(12f, 3f, 2.5f), PerspectiveMask.Pedestrian);

            // DRIVER: pedestrian waiting at the zebra steps out.
            var zebraPed = ScriptedPedestrian(rig, "Counterpart - zebra pedestrian", Path(rig.paths, "Scripted zebra ped",
                new Vector3(1.2f, 0f, -4.4f), new Vector3(1.2f, 0f, 4.4f), new Vector3(1.2f, 0f, 6f), new Vector3(10f, 0f, 6f)), Perspective.Driver);
            var h1 = Hazard(rig, HZ("zebra_ped_steps_out"), HazardTriggerMode.PlayerWithinDistance, zebra.transform.position);
            h1.DistancePoint = zebra.transform;
            h1.Distance = 40f;
            h1.StopPoint = ebLine.transform;
            Act(h1, zebraPed, HazardActorMode.CrossNow, 1f);

            // DRIVER: jaywalker darts out after the crossing.
            var jayPed = ScriptedPedestrian(rig, "Counterpart - jaywalker", Path(rig.paths, "Scripted jaywalk",
                new Vector3(32f, 0f, 4.4f), new Vector3(32f, 0f, -4.4f), new Vector3(32f, 0f, -6f), new Vector3(45f, 0f, -6f)), Perspective.Driver, "B");
            var h2 = Hazard(rig, HZ("zebra_jaywalker"), HazardTriggerMode.PlayerWithinDistance, new Vector3(32f, 0f, -Lane));
            h2.DistancePoint = h2.transform;
            h2.Distance = 24f;
            Act(h2, jayPed, HazardActorMode.CrossNow, 1.7f);

            // PEDESTRIAN: a car that does not stop at the zebra.
            var noYieldCar = ScriptedVehicle(rig, RoadUserKind.Car, "Counterpart - car not yielding", eb, 110f, 45f, true, true, Perspective.Pedestrian, yields: false);
            var h3 = Hazard(rig, HZ("zebra_car_no_yield"), HazardTriggerMode.PlayerWithinDistance, zebra.KerbA.position);
            h3.DistancePoint = zebra.KerbA;
            h3.Distance = 2.5f;
            Act(h3, noYieldCar, HazardActorMode.DoNotYield);
            Act(h3, noYieldCar, HazardActorMode.Activate, 45f);

            // PEDESTRIAN: moto-taxi in the far lane.
            var farMoto = ScriptedVehicle(rig, RoadUserKind.MotoTaxi, "Counterpart - far-lane moto", wb, 115f, 40f, true, true, Perspective.Pedestrian, yields: false);
            var h4 = Hazard(rig, HZ("zebra_moto_far_lane"), HazardTriggerMode.PlayerEntersVolume, zebra.transform.position);
            h4.VolumeSize = new Vector3(4f, 3f, 7f);
            Act(h4, farMoto, HazardActorMode.Activate, 40f, 0.3f);

            Save(rig, name);
        }

        // ================================================================ B: signalised intersection

        public static void BuildSignalized(string defPath)
        {
            const string name = "RR_SignalizedIntersection";
            var rig = NewScene(name, defPath);
            const float h = 160f;

            // Roads.
            RoadRect(rig.env, "E-W road", -h, h, -HalfRoad, HalfRoad);
            RoadRect(rig.env, "N-S road north", -HalfRoad, HalfRoad, HalfRoad, h);
            RoadRect(rig.env, "N-S road south", -HalfRoad, HalfRoad, -h, -HalfRoad);
            Zone(rig.zones, "E-W carriageway", RoadZoneType.Carriageway, -h, h, -HalfRoad, HalfRoad);
            Zone(rig.zones, "N-S carriageway", RoadZoneType.Carriageway, -HalfRoad, HalfRoad, -h, h);
            Zone(rig.zones, "Junction", RoadZoneType.Junction, -HalfRoad, HalfRoad, -HalfRoad, HalfRoad);
            foreach (var (n, x0, x1, z0, z1) in new[]
                     {
                         ("SW-a", -h, -HalfRoad, -6.5f, -HalfRoad), ("SE-a", HalfRoad, h, -6.5f, -HalfRoad),
                         ("NW-a", -h, -HalfRoad, HalfRoad, 6.5f), ("NE-a", HalfRoad, h, HalfRoad, 6.5f),
                         ("SW-b", -6.5f, -HalfRoad, -h, -6.5f), ("SE-b", HalfRoad, 6.5f, -h, -6.5f),
                         ("NW-b", -6.5f, -HalfRoad, 6.5f, h), ("NE-b", HalfRoad, 6.5f, 6.5f, h),
                     })
                SidewalkRect(rig.env, rig.zones, "Sidewalk " + n, x0, x1, z0, z1);
            CentreLineX(rig.env, -h, -11f);
            CentreLineX(rig.env, 11f, h);
            CentreLineZ(rig.env, -h, -11f);
            CentreLineZ(rig.env, 11f, h);
            Buildings(rig.env, -h, -8f, 6.8f, +1, 21);
            Buildings(rig.env, 8f, h, 6.8f, +1, 22);
            Buildings(rig.env, -h, -8f, -6.8f, -1, 23);
            Buildings(rig.env, 8f, h, -6.8f, -1, 24);

            // Signals: groups ew / ns vehicles, ped = pedestrians crossing the E-W road.
            var signal = Group(rig.rules, "Signal controller").gameObject.AddComponent<TrafficSignalController>();
            void Phase(string n, float d, SignalState ew, SignalState ns, SignalState ped)
            {
                var p = new TrafficSignalController.Phase { name = n, duration = d };
                p.states.Add(new TrafficSignalController.GroupState { group = "ew", state = ew });
                p.states.Add(new TrafficSignalController.GroupState { group = "ns", state = ns });
                p.states.Add(new TrafficSignalController.GroupState { group = "ped", state = ped });
                signal.Phases.Add(p);
            }

            Phase("EW green", 14f, SignalState.Green, SignalState.Red, SignalState.Red);
            Phase("EW amber", 3f, SignalState.Amber, SignalState.Red, SignalState.Red);
            Phase("All red", 2f, SignalState.Red, SignalState.Red, SignalState.Red);
            Phase("NS green + walk", 12f, SignalState.Red, SignalState.Green, SignalState.Green);
            Phase("Walk ending", 4f, SignalState.Red, SignalState.Green, SignalState.Amber);
            Phase("NS amber", 3f, SignalState.Red, SignalState.Amber, SignalState.Red);
            Phase("All red", 2f, SignalState.Red, SignalState.Red, SignalState.Red);
            EditorUtility.SetDirty(signal);
            rig.ctx.Signals.Add(signal);
            // ~3.5 s of fade + countdown elapse before t=0. Driver meets red, then green at t~16 s;
            // pedestrian meets "don't walk", then walk at t~7.5 s.
            rig.ctx.SignalOffsetDriver = 20.5f;
            rig.ctx.SignalOffsetPedestrian = 7.5f;

            var west = ZebraAcrossEW(rig.env, rig.zones, "West crossing", -7.5f, HalfRoad, 3f, RoadZoneType.SignalizedCrossing);
            var east = ZebraAcrossEW(rig.env, rig.zones, "East crossing", 7.5f, HalfRoad, 3f, RoadZoneType.SignalizedCrossing);
            foreach (var c in new[] { west, east })
            {
                c.Signal = signal;
                c.PedestrianSignalGroup = "ped";
            }

            var ebLine = StopLine(rig.rules, rig.env, "Stop EB", new Vector3(-10f, 0f, -Lane), Vector3.right, StopLineType.Signal, signal: signal, group: "ew");
            StopLine(rig.rules, rig.env, "Stop WB", new Vector3(10f, 0f, Lane), Vector3.left, StopLineType.Signal, signal: signal, group: "ew");
            StopLine(rig.rules, rig.env, "Stop SB", new Vector3(-Lane, 0f, 10f), Vector3.back, StopLineType.Signal, signal: signal, group: "ns");
            StopLine(rig.rules, rig.env, "Stop NB", new Vector3(Lane, 0f, -10f), Vector3.forward, StopLineType.Signal, signal: signal, group: "ns");
            var eastYield = StopLine(rig.rules, rig.env, "Yield to crossing peds EB", new Vector3(5.6f, 0f, -Lane), Vector3.right, StopLineType.ZebraYield, crossing: east);
            StopLine(rig.rules, rig.env, "Yield to crossing peds WB", new Vector3(-5.6f, 0f, Lane), Vector3.left, StopLineType.ZebraYield, crossing: west);

            SignalHead(rig.env, "Signal EB", new Vector3(-10.5f, 0f, -4.2f), Vector3.left, signal, "ew", false);
            SignalHead(rig.env, "Signal WB", new Vector3(10.5f, 0f, 4.2f), Vector3.right, signal, "ew", false);
            SignalHead(rig.env, "Signal SB", new Vector3(-4.2f, 0f, 10.5f), Vector3.forward, signal, "ns", false);
            SignalHead(rig.env, "Signal NB", new Vector3(4.2f, 0f, -10.5f), Vector3.back, signal, "ns", false);
            SignalHead(rig.env, "Ped signal W-S", new Vector3(-9.3f, SidewalkTop, -4.3f), Vector3.forward, signal, "ped", true);
            SignalHead(rig.env, "Ped signal W-N", new Vector3(-5.7f, SidewalkTop, 4.3f), Vector3.back, signal, "ped", true);
            SignalHead(rig.env, "Ped signal E-S", new Vector3(5.7f, SidewalkTop, -4.3f), Vector3.forward, signal, "ped", true);
            SignalHead(rig.env, "Ped signal E-N", new Vector3(9.3f, SidewalkTop, 4.3f), Vector3.back, signal, "ped", true);

            var (eb, wb) = MainLanes(rig, 2f * h);
            var sb = Path(rig.paths, "Lane SB", new Vector3(-Lane, 0f, h), new Vector3(-Lane, 0f, -h));
            var nb = Path(rig.paths, "Lane NB", new Vector3(Lane, 0f, -h), new Vector3(Lane, 0f, h));
            var nbLeft = Path(rig.paths, "NB turn left to WB", new Vector3(Lane, 0f, -60f), new Vector3(Lane, 0f, -4f), new Vector3(1.2f, 0f, -1.5f),
                new Vector3(0f, 0f, 0.8f), new Vector3(-2f, 0f, 1.6f), new Vector3(-4f, 0f, Lane), new Vector3(-60f, 0f, Lane));

            Spawner(rig, "Traffic EB", eb, 0.6f, 0);
            Spawner(rig, "Traffic WB", wb, 1f, 2);
            Spawner(rig, "Traffic SB", sb, 0.7f, 2);
            Spawner(rig, "Traffic NB", nb, 0.7f, 2);
            var pW = Path(rig.paths, "Ped west S->N", new Vector3(-30f, 0f, -5f), new Vector3(-7.9f, 0f, -5f), new Vector3(-7.9f, 0f, 5f), new Vector3(-30f, 0f, 5f));
            var pE = Path(rig.paths, "Ped east N->S", new Vector3(30f, 0f, 5f), new Vector3(7.1f, 0f, 5f), new Vector3(7.1f, 0f, -5f), new Vector3(30f, 0f, -5f));
            var jay = Path(rig.paths, "Ped jaywalk", new Vector3(-40f, 0f, -5f), new Vector3(-25f, 0f, -5f), new Vector3(-25f, 0f, 5f), new Vector3(-50f, 0f, 5f));
            PedSpawner(rig, new[] { pW, pE }, new[] { jay });

            PlayerCar(rig, new Vector3(-120f, 0f, -Lane), 90f);
            Goal(rig, "Driver goal", new Vector3(60f, 0f, -Lane), new Vector3(6f, 3f, 7f), PerspectiveMask.Driver, false);
            PedestrianSpawn(rig, new Vector3(-12f, SidewalkTop, -5.2f), 90f);
            Goal(rig, "Pedestrian goal", new Vector3(-7.5f, 0f, 6.5f), new Vector3(8f, 3f, 3f), PerspectiveMask.Pedestrian);

            // DRIVER: red-light runner from the north when your light turns green.
            var runner = ScriptedVehicle(rig, RoadUserKind.Car, "Counterpart - red-light runner", sb, 100f, 50f, true, true, Perspective.Driver, obeys: false);
            var h5 = Hazard(rig, HZ("signal_red_runner"), HazardTriggerMode.PlayerWithinDistance, Vector3.zero);
            h5.DistancePoint = h5.transform;
            h5.Distance = 30f;
            h5.SetSignalCondition(signal, "ew", SignalState.Green);
            Act(h5, runner, HazardActorMode.IgnoreSignals);
            Act(h5, runner, HazardActorMode.Activate, 50f);

            // DRIVER: slow pedestrian still on the east crossing when your light turns green.
            var latePed = ScriptedPedestrian(rig, "Counterpart - late pedestrian", Path(rig.paths, "Scripted late ped",
                new Vector3(7.5f, 0f, 4.4f), new Vector3(7.5f, 0f, -4.4f), new Vector3(7.5f, 0f, -8f), new Vector3(7.5f, 0f, -30f)), Perspective.Driver, "B");
            var h6 = Hazard(rig, HZ("signal_late_pedestrian"), HazardTriggerMode.PlayerWithinDistance, Vector3.zero);
            h6.DistancePoint = h6.transform;
            h6.Distance = 25f;
            h6.StopPoint = eastYield.transform;
            h6.SetSignalCondition(signal, "ew", SignalState.Green);
            Act(h6, latePed, HazardActorMode.CrossNow, 0.8f, 1f);

            // PEDESTRIAN: moto-taxi runs the red just as the walk signal turns green.
            var redMoto = ScriptedVehicle(rig, RoadUserKind.MotoTaxi, "Counterpart - red-running moto", eb, 105f, 45f, true, true, Perspective.Pedestrian, obeys: false, yields: false);
            var h7 = Hazard(rig, HZ("signal_red_runner_ped"), HazardTriggerMode.PlayerWithinDistance, west.KerbA.position);
            h7.DistancePoint = west.KerbA;
            h7.Distance = 2.5f;
            h7.SetSignalCondition(signal, "ped", SignalState.Green);
            Act(h7, redMoto, HazardActorMode.IgnoreSignals);
            Act(h7, redMoto, HazardActorMode.Activate, 45f);

            // PEDESTRIAN: left-turning car cuts across the crossing during the walk phase.
            var turner = ScriptedVehicle(rig, RoadUserKind.Car, "Counterpart - turning car", nbLeft, 30f, 30f, true, true, Perspective.Pedestrian, obeys: false, yields: false);
            var h8 = Hazard(rig, HZ("signal_turning_car"), HazardTriggerMode.PlayerEntersVolume, west.transform.position);
            h8.VolumeSize = new Vector3(3f, 3f, 7f);
            Act(h8, turner, HazardActorMode.Activate, 30f);

            Save(rig, name);
        }

        // ================================================================ C: unsignalised (moto-taxi) T-junction

        public static void BuildJunction(string defPath)
        {
            const string name = "RR_MotoTaxiJunction";
            var rig = NewScene(name, defPath);
            const float h = 160f;
            MainRoad(rig, 2f * h, southGapForSideRoad: true);
            RoadRect(rig.env, "Side road", -HalfRoad, HalfRoad, -h, -HalfRoad);
            Zone(rig.zones, "Side carriageway", RoadZoneType.Carriageway, -HalfRoad, HalfRoad, -h, -HalfRoad);
            Zone(rig.zones, "Junction", RoadZoneType.Junction, -HalfRoad, HalfRoad, -HalfRoad, HalfRoad);
            SidewalkRect(rig.env, rig.zones, "Side sidewalk W", -6.5f, -HalfRoad, -h, -6.5f);
            SidewalkRect(rig.env, rig.zones, "Side sidewalk E", HalfRoad, 6.5f, -h, -6.5f);
            CentreLineX(rig.env, -h, h, 0f, -5f, 5f);
            CentreLineZ(rig.env, -h, -6f);
            BuildingsZ(rig.env, -h, -12f, -6.8f, -1, 31);
            BuildingsZ(rig.env, -h, -12f, 6.8f, +1, 32);

            var conflict = Point(rig.rules, "Conflict point", Vector3.zero);
            var giveWay = StopLine(rig.rules, rig.env, "Give way NB", new Vector3(Lane, 0f, -4.5f), Vector3.forward, StopLineType.GiveWay, conflict: conflict);
            var sideSpeed = Point(rig.rules, "Side road 30 zone", new Vector3(0f, 0f, -60f)).gameObject.AddComponent<SpeedLimitZone>();
            sideSpeed.Size = new Vector3(7f, 3f, 110f);
            sideSpeed.LimitKmh = 30f;
            var blind = Point(rig.rules, "Blind-spot check before left turn", new Vector3(Lane, 0f, -11f)).gameObject.AddComponent<BlindSpotCheckZone>();
            blind.Size = new Vector3(3.5f, 3f, 12f);
            blind.Side = TurnSide.Left;
            blind.Title = "Blind-spot check before turning left";

            // Parked minibus hiding the left approach from the give-way line.
            Box(rig.env, "Parked minibus (occluder)", new Vector3(-11f, 1.3f, -5.2f), new Vector3(2.2f, 2.6f, 6f), "bus", collider: true, rotation: Quaternion.Euler(0f, 90f, 0f));

            var (eb, wb) = MainLanes(rig, 2f * h);
            var nbLeft = Path(rig.paths, "Side NB turn left", new Vector3(Lane, 0f, -h), new Vector3(Lane, 0f, -4f), new Vector3(1.2f, 0f, -1f),
                new Vector3(0f, 0f, 0.9f), new Vector3(-2f, 0f, 1.6f), new Vector3(-4f, 0f, Lane), new Vector3(-h, 0f, Lane));
            var nbRight = Path(rig.paths, "Side NB turn right", new Vector3(Lane, 0f, -h), new Vector3(Lane, 0f, -5f), new Vector3(2.5f, 0f, -2.6f),
                new Vector3(4f, 0f, -Lane), new Vector3(h, 0f, -Lane));
            var ebRight = Path(rig.paths, "Main EB turn right into side", new Vector3(-h, 0f, -Lane), new Vector3(-5f, 0f, -Lane), new Vector3(-2.6f, 0f, -2.5f),
                new Vector3(-Lane, 0f, -4f), new Vector3(-Lane, 0f, -h));

            Spawner(rig, "Traffic EB (priority)", eb, 1f, 3);
            Spawner(rig, "Traffic WB (priority)", wb, 1f, 3);
            Spawner(rig, "Traffic EB into side road", ebRight, 0.25f, 0);
            var pedE = Path(rig.paths, "Ped S sidewalk E", new Vector3(-40f, 0f, -5f), new Vector3(40f, 0f, -5f));
            var pedW = Path(rig.paths, "Ped S sidewalk W", new Vector3(40f, 0f, -5.8f), new Vector3(-40f, 0f, -5.8f));
            var pedN = Path(rig.paths, "Ped N sidewalk", new Vector3(-60f, 0f, 5.2f), new Vector3(60f, 0f, 5.2f));
            var jay = Path(rig.paths, "Ped jaywalk", new Vector3(-30f, 0f, 5.4f), new Vector3(-20f, 0f, 5.4f), new Vector3(-20f, 0f, -5.4f), new Vector3(-40f, 0f, -5.4f));
            PedSpawner(rig, new[] { pedE, pedW, pedN }, new[] { jay });

            PlayerCar(rig, new Vector3(Lane, 0f, -90f), 0f);
            Goal(rig, "Driver goal (after left turn)", new Vector3(-50f, 0f, Lane), new Vector3(6f, 3f, 3.5f), PerspectiveMask.Driver, false);
            PedestrianSpawn(rig, new Vector3(-9f, SidewalkTop, -5.2f), 90f);
            Goal(rig, "Pedestrian goal", new Vector3(9f, 0f, -5.2f), new Vector3(5f, 3f, 3f), PerspectiveMask.Pedestrian);

            // DRIVER: moto-taxi ahead stops suddenly for a passenger.
            var pickup = ScriptedVehicle(rig, RoadUserKind.MotoTaxi, "Counterpart - pickup moto", nbRight, 90f, 25f, false, false, Perspective.Driver);
            var passenger = ScriptedPedestrian(rig, "Waiting passenger (static)", Path(rig.paths, "Passenger spot", new Vector3(4.3f, 0f, -45f), new Vector3(4.3f, 0f, -44.5f)), Perspective.Driver);
            passenger.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
            var h9 = Hazard(rig, HZ("junction_moto_sudden_stop"), HazardTriggerMode.PlayerWithinDistance, pickup.transform.position);
            h9.DistancePoint = pickup.transform;
            h9.Distance = 16f;
            Act(h9, pickup, HazardActorMode.SuddenStop, 3.5f);

            // DRIVER: fast moto-taxi hidden behind the parked minibus.
            var hidden = ScriptedVehicle(rig, RoadUserKind.MotoTaxi, "Counterpart - hidden moto", eb, 110f, 50f, true, true, Perspective.Driver, yields: false);
            var h10 = Hazard(rig, HZ("junction_hidden_moto"), HazardTriggerMode.PlayerWithinDistance, giveWay.transform.position);
            h10.DistancePoint = giveWay.transform;
            h10.Distance = 8f;
            Act(h10, hidden, HazardActorMode.Activate, 50f);

            // PEDESTRIAN: car turns into the side road without signalling.
            var turnIn = ScriptedVehicle(rig, RoadUserKind.Car, "Counterpart - turning-in car", ebRight, 135f, 32f, true, true, Perspective.Pedestrian, yields: false);
            var kerbWest = Point(rig.rules, "Side road west kerb", new Vector3(-3.8f, 0f, -5.2f));
            var h11 = Hazard(rig, HZ("junction_turning_car_ped"), HazardTriggerMode.PlayerWithinDistance, kerbWest.position);
            h11.DistancePoint = kerbWest;
            h11.Distance = 2.5f;
            Act(h11, turnIn, HazardActorMode.Activate, 32f);

            // PEDESTRIAN: moto-taxi pulls out of the side road while you cross its mouth.
            var exitMoto = ScriptedVehicle(rig, RoadUserKind.MotoTaxi, "Counterpart - exiting moto", nbRight, 120f, 35f, true, true, Perspective.Pedestrian, yields: false);
            var h12 = Hazard(rig, HZ("junction_moto_exit"), HazardTriggerMode.PlayerEntersVolume, new Vector3(0f, 0f, -5.2f));
            h12.VolumeSize = new Vector3(7f, 3f, 3f);
            Act(h12, exitMoto, HazardActorMode.Activate, 35f);

            Save(rig, name);
        }
    }
}
