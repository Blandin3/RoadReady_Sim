using System.Collections.Generic;
using RoadReady.Core;
using RoadReady.Hazards;
using RoadReady.Traffic;
using UnityEngine;
using static RoadReady.EditorTools.GreyboxKit;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// D: the open road - an endless loop (rounded rectangle) the learner drives round for the whole attempt.
    /// Driving is anticlockwise in the outer lane (right-hand traffic: the outside of the loop is on your right).
    /// Five hazard stations re-arm at random each lap through a RecurringHazardDirector.
    /// </summary>
    public static partial class ScenarioSceneBuilder
    {
        // Loop: half-extents of the centreline rectangle and its corner radius (metres). Lap ~ 870 m.
        // With the street pack the loop is a city block of whole road tiles round the pack's corner piece
        // (centre-line radius 8.75 m); otherwise a wide rounded rectangle. Lap ~ 900 m either way.
        static float LoopR => StreetKit.Available ? StreetKit.TurnRadius : 30f;
        static float LoopA => StreetKit.Available ? LoopR + 16 * StreetKit.TileLength : 150f;
        static float LoopB => StreetKit.Available ? LoopR + 8 * StreetKit.TileLength : 80f;

        class Loop
        {
            public readonly List<Vector3> points = new List<Vector3>();
            readonly List<float> m_Cumulative = new List<float>();
            public float Length { get; private set; }

            public void Close()
            {
                m_Cumulative.Clear();
                var total = 0f;
                for (var i = 0; i < points.Count; i++)
                {
                    if (i > 0) total += Vector3.Distance(points[i - 1], points[i]);
                    m_Cumulative.Add(total);
                }

                Length = total + Vector3.Distance(points[points.Count - 1], points[0]);
            }

            /// <summary>Position and direction of travel at a distance along the centreline (wraps around).</summary>
            public (Vector3 pos, Vector3 fwd) At(float s)
            {
                s = Mathf.Repeat(s, Length);
                for (var i = 0; i < points.Count; i++)
                {
                    var a = points[i];
                    var b = points[(i + 1) % points.Count];
                    var start = m_Cumulative[i];
                    var segment = Vector3.Distance(a, b);
                    if (s <= start + segment || i == points.Count - 1)
                    {
                        var t = segment > 0f ? Mathf.Clamp01((s - start) / segment) : 0f;
                        return (Vector3.Lerp(a, b, t), (b - a).normalized);
                    }
                }

                return (points[0], Vector3.right);
            }

            /// <summary>Point at distance s, offset sideways (positive = right of travel = outside of the loop).</summary>
            public Vector3 Lat(float s, float lateral)
            {
                var (pos, fwd) = At(s);
                return pos + new Vector3(fwd.z, 0f, -fwd.x) * lateral;
            }

            public float Yaw(float s) => GreyboxKit.Yaw(At(s).fwd);
        }

        static Loop BuildLoopCentre()
        {
            var loop = new Loop();
            void Line(Vector3 a, Vector3 b)
            {
                var n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 10f));
                for (var i = 0; i < n; i++)
                    loop.points.Add(Vector3.Lerp(a, b, i / (float)n));
            }

            void Arc(Vector3 centre, float fromDeg, float toDeg)
            {
                const int n = 15;
                for (var i = 0; i < n; i++)
                {
                    var a = Mathf.Lerp(fromDeg, toDeg, i / (float)n) * Mathf.Deg2Rad;
                    loop.points.Add(centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * LoopR);
                }
            }

            float A = LoopA, B = LoopB, R = LoopR;
            // Start at the middle of the south side heading east, then anticlockwise.
            Line(new Vector3(0f, 0f, -B), new Vector3(A - R, 0f, -B));
            Arc(new Vector3(A - R, 0f, -(B - R)), -90f, 0f);
            Line(new Vector3(A, 0f, -(B - R)), new Vector3(A, 0f, B - R));
            Arc(new Vector3(A - R, 0f, B - R), 0f, 90f);
            Line(new Vector3(A - R, 0f, B), new Vector3(-(A - R), 0f, B));
            Arc(new Vector3(-(A - R), 0f, B - R), 90f, 180f);
            Line(new Vector3(-A, 0f, B - R), new Vector3(-A, 0f, -(B - R)));
            Arc(new Vector3(-(A - R), 0f, -(B - R)), 180f, 270f);
            Line(new Vector3(-(A - R), 0f, -B), new Vector3(0f, 0f, -B));
            loop.Close();
            return loop;
        }

        /// <summary>Unscored trigger used only to choreograph an actor (e.g. a moto-taxi pulling out before its scored stop).</summary>
        static HazardTrigger Choreography(Rig rig, string name, Transform distancePoint, float distance)
        {
            var trigger = Point(rig.hazards, "Choreography - " + name, distancePoint.position).gameObject.AddComponent<HazardTrigger>();
            trigger.ActiveFor = PerspectiveMask.Driver;
            trigger.Mode = HazardTriggerMode.PlayerWithinDistance;
            trigger.DistancePoint = distancePoint;
            trigger.Distance = distance;
            return trigger;
        }

        /// <summary>Zebra crossing at loop distance s (on a straight), with yield lines for both lanes and a 30 zone.</summary>
        static (RoadZone zone, Traffic.StopLine playerLine) LoopZebra(Rig rig, Loop loop, string name, float s)
        {
            const float width = 4f;
            var (centre, fwd) = loop.At(s);
            var yaw = loop.Yaw(s);
            var right = new Vector3(fwd.z, 0f, -fwd.x);

            var markings = Point(rig.env, name + " Markings", centre, yaw);
            var alongX = Mathf.Abs(fwd.x) > 0.5f;
            // RoadMeshKit meshes are in world coordinates, so the patch goes under the (unrotated) environment root.
            if (!StreetKit.Available)
                RoadMeshKit.PlainPatch(rig.env, name + " plain asphalt", centre, alongX ? width + 1f : 7f, alongX ? 7f : width + 1f);
            for (var lat = -HalfRoad + 0.5f; lat <= HalfRoad - 0.4f; lat += 1f)
                Box(markings, "Stripe", new Vector3(lat, 0.005f, 0f), new Vector3(0.5f, 0.01f, width), "marking");

            var zoneT = Point(rig.zones, name, centre, yaw);
            var zone = zoneT.gameObject.AddComponent<RoadZone>();
            zone.Configure(RoadZoneType.ZebraCrossing, new Vector3(2f * HalfRoad, 3f, width));
            zone.KerbA = Point(zoneT, "Kerb A (outer)", centre + right * (HalfRoad + 0.3f));
            zone.KerbB = Point(zoneT, "Kerb B (inner)", centre - right * (HalfRoad + 0.3f));

            var playerLine = StopLine(rig.rules, rig.env, name + " yield (your lane)", loop.Lat(s - 5.5f, Lane), fwd, StopLineType.ZebraYield, crossing: zone);
            StopLine(rig.rules, rig.env, name + " yield (oncoming)", loop.Lat(s + 5.5f, -Lane), -fwd, StopLineType.ZebraYield, crossing: zone);

            var slow = Point(rig.rules, name + " 30 zone", centre, yaw).gameObject.AddComponent<SpeedLimitZone>();
            slow.Size = new Vector3(2f * HalfRoad, 3f, 60f);
            slow.LimitKmh = 30f;
            return (zone, playerLine);
        }

        static WaypointPath LoopPath(Rig rig, string name, Loop loop, float lateral, bool reverse)
        {
            var points = new List<Vector3>();
            var rights = RoadMeshKit.Rights(loop.points, true);
            for (var i = 0; i < loop.points.Count; i++)
                points.Add(loop.points[i] + rights[i] * lateral);
            if (reverse)
                points.Reverse();
            var path = Path(rig.paths, name, points.ToArray());
            path.Loop = true;
            return path;
        }

        // ================================================================ D: open road (endless loop)

        public static void BuildOpenRoad(string defPath)
        {
            const string name = "RR_OpenRoad";
            var rig = NewScene(name, defPath);
            var loop = BuildLoopCentre();
            var L = loop.Length;
            float A = LoopA, B = LoopB, R = LoopR;
            // Where each side starts along the loop (anticlockwise from the middle of the south side).
            var quarter = Mathf.PI * R * 0.5f;
            var eastStart = (A - R) + quarter;
            var northStart = eastStart + 2f * (B - R) + quarter;
            var westStart = northStart + 2f * (A - R) + quarter;

            // ---------------------------------------------------------------- road, sidewalks, zones
            if (StreetKit.Available)
            {
                var alongX = Mathf.RoundToInt(2f * (A - R) / StreetKit.TileLength);
                var alongZ = Mathf.RoundToInt(2f * (B - R) / StreetKit.TileLength);
                StreetKit.Straight(rig.env, new Vector3(-(A - R), 0f, -B), Vector3.right, alongX);
                StreetKit.Straight(rig.env, new Vector3(A, 0f, -(B - R)), Vector3.forward, alongZ);
                StreetKit.Straight(rig.env, new Vector3(A - R, 0f, B), Vector3.left, alongX);
                StreetKit.Straight(rig.env, new Vector3(-A, 0f, B - R), Vector3.back, alongZ);
                // Corner arcs centred inside the block; yaw turns the tile to join the right pair of arms.
                foreach (var (centre, yaw, diagonal) in new[]
                         {
                             (new Vector3(A - R, 0f, -(B - R)), 180f, new Vector3(1f, 0f, -1f)),
                             (new Vector3(A - R, 0f, B - R), 90f, new Vector3(1f, 0f, 1f)),
                             (new Vector3(-(A - R), 0f, B - R), 0f, new Vector3(-1f, 0f, 1f)),
                             (new Vector3(-(A - R), 0f, -(B - R)), 270f, new Vector3(-1f, 0f, -1f)),
                         })
                {
                    StreetKit.Corner(rig.env, centre, yaw);
                    // Tight city corner: 20 km/h.
                    var slowCorner = Point(rig.rules, "Corner 20 zone", centre + diagonal.normalized * R, 45f).gameObject.AddComponent<SpeedLimitZone>();
                    slowCorner.Size = new Vector3(18f, 3f, 18f);
                    slowCorner.LimitKmh = 20f;
                }

                for (var s = 25f; s < L; s += 40f)
                {
                    var kerb = loop.Lat(s, 7.6f);
                    StreetKit.StreetLight(rig.env, kerb + Vector3.up * SidewalkTop, loop.Lat(s, 0f) - kerb);
                }
            }
            else
            {
                RoadMeshKit.Ribbon(rig.env, "Loop road", loop.points, 2f * HalfRoad, true, true);
                RoadMeshKit.Sidewalk(rig.env, "Sidewalk outer", loop.points, true, HalfRoad, 6.5f);
                RoadMeshKit.Sidewalk(rig.env, "Sidewalk inner", loop.points, true, -HalfRoad, -6.5f);
            }
            var carriageway = Group(rig.zones, "Carriageway");
            RoadMeshKit.ZonesAlong(carriageway, "Carriageway", RoadZoneType.Carriageway, loop.points, true, 0f, 2f * HalfRoad);
            var walks = Group(rig.zones, "Sidewalks");
            RoadMeshKit.ZonesAlong(walks, "Sidewalk outer", RoadZoneType.Sidewalk, loop.points, true, 5f, 3f);
            RoadMeshKit.ZonesAlong(walks, "Sidewalk inner", RoadZoneType.Sidewalk, loop.points, true, -5f, 3f);

            // ---------------------------------------------------------------- town
            // Driveway gaps on the outer side of the north street (where the car pulls out / parks).
            var drivewayOut = 380f;
            var drivewayIn = 476f;
            float NorthX(float s) => (A - R) - (s - northStart);
            var gaps = new[] { new Vector2(NorthX(drivewayOut) - 5f, NorthX(drivewayOut) + 5f), new Vector2(NorthX(drivewayIn) - 5f, NorthX(drivewayIn) + 5f) };
            Buildings(rig.env, -(A - R) + 15f, (A - R) - 15f, -B - BuildingSetback, -1, 41);
            Buildings(rig.env, -(A - R) + 15f, (A - R) - 15f, -B + BuildingSetback, +1, 42);
            Buildings(rig.env, -(A - R) + 15f, (A - R) - 15f, B + BuildingSetback, +1, 43, gaps);
            Buildings(rig.env, -(A - R) + 15f, (A - R) - 15f, B - BuildingSetback, -1, 44);
            BuildingsZ(rig.env, -(B - R) + 15f, (B - R) - 15f, A + BuildingSetback, +1, 45);
            BuildingsZ(rig.env, -(B - R) + 15f, (B - R) - 15f, A - BuildingSetback, -1, 46);
            BuildingsZ(rig.env, -(B - R) + 15f, (B - R) - 15f, -A - BuildingSetback, -1, 47);
            BuildingsZ(rig.env, -(B - R) + 15f, (B - R) - 15f, -A + BuildingSetback, +1, 48);
            for (var s = 15f; s < L; s += 32f)
            {
                Tree(rig.env, loop.Lat(s, 5.9f) + Vector3.up * SidewalkTop);
                Tree(rig.env, loop.Lat(s + 16f, -5.9f) + Vector3.up * SidewalkTop);
            }

            // ---------------------------------------------------------------- crossings
            var zebraSouthS = 40f;
            var zebraWestS = westStart + (B - R);
            var (zebraSouth, zebraSouthLine) = LoopZebra(rig, loop, "Zebra south", zebraSouthS);
            var (zebraWest, zebraWestLine) = LoopZebra(rig, loop, "Zebra west", zebraWestS);

            // ---------------------------------------------------------------- traffic & people
            var yourLane = LoopPath(rig, "Lane anticlockwise (your lane)", loop, Lane, false);
            var oncoming = LoopPath(rig, "Lane clockwise (oncoming)", loop, -Lane, true);
            var ownTraffic = Spawner(rig, "Traffic in your lane", yourLane, 0.5f, 4);
            ownTraffic.MaxActive = 5;
            var oppositeTraffic = Spawner(rig, "Oncoming traffic", oncoming, 1f, 6);
            oppositeTraffic.MaxActive = 8;

            WaypointPath CrossPath(string n, float s, float side) => Path(rig.paths, n,
                loop.Lat(s - 35f, side * 5.2f), loop.Lat(s - 0.8f, side * 5.2f), loop.Lat(s - 0.8f, -side * 5.2f), loop.Lat(s - 35f, -side * 5.2f));
            WaypointPath WalkPath(string n, float from, float to, float lateral) => Path(rig.paths, n, loop.Lat(from, lateral), loop.Lat(to, lateral));
            PedSpawner(rig,
                new[]
                {
                    CrossPath("Ped across zebra south", zebraSouthS, 1f), CrossPath("Ped across zebra south (back)", zebraSouthS + 1.6f, -1f),
                    CrossPath("Ped across zebra west", zebraWestS, -1f),
                    WalkPath("Ped walk south outer", -100f, 100f, 5.4f), WalkPath("Ped walk north inner", northStart + 20f, northStart + 220f, -5.4f),
                    WalkPath("Ped walk east outer", eastStart + 5f, eastStart + 95f, 5.4f),
                },
                new[] { Path(rig.paths, "Ped jaywalk south", loop.Lat(-60f, -5.4f), loop.Lat(-50f, -5.4f), loop.Lat(-50f, 5.4f), loop.Lat(-30f, 5.4f)) });

            PlayerCar(rig, loop.Lat(L - 110f, Lane), loop.Yaw(L - 110f));
            // No goal: the drive ends on the time limit.

            var director = Group(rig.root, "Recurring Hazard Director").gameObject.AddComponent<RecurringHazardDirector>();
            director.Loop = yourLane;
            RecurringHazardDirector.Station Station(string n, Vector3 anchor, float chance)
            {
                var station = new RecurringHazardDirector.Station { name = n, anchor = Point(rig.hazards, "Station - " + n, anchor), chancePerLap = chance };
                director.Stations.Add(station);
                return station;
            }

            // 1. Pedestrian steps onto the south zebra from your side.
            {
                var ped = ScriptedPedestrian(rig, "Counterpart - zebra south pedestrian", Path(rig.paths, "Scripted zebra south ped",
                    loop.Lat(zebraSouthS + 1.2f, 4.4f), loop.Lat(zebraSouthS + 1.2f, -4.4f), loop.Lat(zebraSouthS + 1.2f, -6f), loop.Lat(zebraSouthS + 14f, -6f)), Perspective.Driver);
                var h = Hazard(rig, HZ("open_ped_steps_out"), HazardTriggerMode.PlayerWithinDistance, zebraSouth.transform.position);
                h.DistancePoint = zebraSouth.transform;
                h.Distance = 40f;
                h.StopPoint = zebraSouthLine.transform;
                Act(h, ped, HazardActorMode.CrossNow, 1f);
                var st = Station("Zebra south", zebraSouth.transform.position, 0.7f);
                st.triggers.Add(h);
                st.actors.Add(ped);
            }

            // 2. Moto-taxi pulls away from the kerb ahead of you, then stops suddenly for a waiting passenger.
            {
                var s0 = eastStart + 18f;
                var stopAt = eastStart + 58f;
                var moto = ScriptedVehicle(rig, RoadUserKind.MotoTaxi, "Counterpart - pickup moto", Path(rig.paths, "Scripted pickup moto",
                        loop.Lat(s0, 4f), loop.Lat(s0 + 10f, Lane), loop.Lat(stopAt + 30f, Lane), loop.Lat(stopAt + 38f, 4f), loop.Lat(stopAt + 41f, 4f)),
                    0f, 25f, true, false, Perspective.Driver);
                moto.DisableAtPathEnd = false;
                var passenger = ScriptedPedestrian(rig, "Waiting passenger (static)", Path(rig.paths, "Passenger spot", loop.Lat(stopAt + 2f, 4.3f), loop.Lat(stopAt + 2.5f, 4.3f)), Perspective.Driver);
                var pullOutAt = Point(rig.hazards, "Moto parked at kerb", loop.Lat(s0, 4f));
                var pullOut = Choreography(rig, "moto pulls away", pullOutAt, 35f);
                Act(pullOut, moto, HazardActorMode.Activate, 25f);
                var h = Hazard(rig, HZ("open_moto_sudden_stop"), HazardTriggerMode.PlayerWithinDistance, loop.Lat(stopAt, Lane));
                h.DistancePoint = moto.transform;
                h.Distance = 14f;
                Act(h, moto, HazardActorMode.SuddenStop, 3.5f);
                var st = Station("Moto-taxi pickup", loop.Lat(stopAt, Lane), 0.65f);
                st.triggers.Add(pullOut);
                st.triggers.Add(h);
                st.actors.Add(moto);
                st.actors.Add(passenger);
            }

            // 3. Car pulls out of a driveway on your right into your lane.
            {
                var car = ScriptedVehicle(rig, RoadUserKind.Car, "Counterpart - driveway car", Path(rig.paths, "Scripted driveway car",
                        loop.Lat(drivewayOut, 9.5f), loop.Lat(drivewayOut, 5f), loop.Lat(drivewayOut + 4f, 2.6f), loop.Lat(drivewayOut + 10f, Lane),
                        loop.Lat(drivewayIn - 6f, Lane), loop.Lat(drivewayIn, 5f), loop.Lat(drivewayIn, 9.5f)),
                    0f, 20f, true, false, Perspective.Driver, yields: false);
                car.DisableAtPathEnd = false;
                var mouth = Point(rig.hazards, "Driveway mouth", loop.Lat(drivewayOut, Lane));
                var h = Hazard(rig, HZ("open_car_pulls_out"), HazardTriggerMode.PlayerWithinDistance, mouth.position);
                h.DistancePoint = mouth;
                h.Distance = 28f;
                Act(h, car, HazardActorMode.Activate, 20f);
                var st = Station("Driveway car", mouth.position, 0.6f);
                st.triggers.Add(h);
                st.actors.Add(car);
            }

            // 4. Pedestrian darts across from the far (left) pavement on the north street.
            {
                var s = northStart + 186f;
                var ped = ScriptedPedestrian(rig, "Counterpart - jaywalker", Path(rig.paths, "Scripted jaywalk north",
                    loop.Lat(s, -4.4f), loop.Lat(s, 4.4f), loop.Lat(s, 6f), loop.Lat(s + 12f, 6f)), Perspective.Driver, "B");
                var at = Point(rig.hazards, "Jaywalk point", loop.Lat(s, Lane));
                var h = Hazard(rig, HZ("open_jaywalker"), HazardTriggerMode.PlayerWithinDistance, at.position);
                h.DistancePoint = at;
                h.Distance = 24f;
                Act(h, ped, HazardActorMode.CrossNow, 1.7f);
                var st = Station("Jaywalker north", at.position, 0.6f);
                st.triggers.Add(h);
                st.actors.Add(ped);
            }

            // 5. Pedestrian steps onto the west zebra from the far (inner) side.
            {
                var ped = ScriptedPedestrian(rig, "Counterpart - zebra west pedestrian", Path(rig.paths, "Scripted zebra west ped",
                    loop.Lat(zebraWestS + 1.2f, -4.4f), loop.Lat(zebraWestS + 1.2f, 4.4f), loop.Lat(zebraWestS + 1.2f, 6f), loop.Lat(zebraWestS + 14f, 6f)), Perspective.Driver, "B");
                var h = Hazard(rig, HZ("open_ped_steps_out"), HazardTriggerMode.PlayerWithinDistance, zebraWest.transform.position);
                h.DistancePoint = zebraWest.transform;
                h.Distance = 40f;
                h.StopPoint = zebraWestLine.transform;
                Act(h, ped, HazardActorMode.CrossNow, 0.9f);
                var st = Station("Zebra west", zebraWest.transform.position, 0.7f);
                st.triggers.Add(h);
                st.actors.Add(ped);
            }

            UnityEditor.EditorUtility.SetDirty(director);
            Save(rig, name);
        }
    }
}
