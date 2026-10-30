using RoadReady.Core;
using RoadReady.Scenarios;
using RoadReady.Traffic;
using UnityEngine;
using static RoadReady.EditorTools.GreyboxKit;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// Tutorial on the imported highway loop (ThirdParty/Highway): the same lesson as the old straight street - the
    /// car starts in the right-hand lane well before a zebra crossing, the pedestrian starts on the pavement just
    /// before it with the kerb marker at the crossing - but the road is an endless loop, so it never runs out.
    /// </summary>
    public static partial class ScenarioSceneBuilder
    {
        const float k_ZebraWidth = 4f;
        const float k_PavementWidth = 3.2f;
        const float k_CarStartBeforeZebra = 150f; // metres along the loop (the old street had 210 m; the loop repeats)
        const float k_PedestrianStartBeforeZebra = 6f;

        static void HighwayTutorial(Rig rig)
        {
            var loop = HighwayLoopKit.Place(rig.env);
            var n = loop.centreLine.Count;
            var step = loop.length / n;
            var hw = loop.meanHalfWidth;

            LoopZones(rig, loop);

            // Zebra on the straightest stretch, built in a frame turned to the road (local +x along the road).
            var zi = HighwayLoopKit.StraightestIndex(loop);
            var dir = HighwayLoopKit.Direction(loop, zi);
            var centre = loop.centreLine[zi];
            centre.y = HighwayLoopKit.SurfaceY(centre) + 0.01f;
            var turn = Quaternion.FromToRotation(Vector3.right, dir);
            var markingsFrame = Group(rig.env, "Tutorial zebra (frame)");
            markingsFrame.SetPositionAndRotation(centre, turn);
            var zoneFrame = Group(rig.zones, "Tutorial zebra zone (frame)");
            zoneFrame.SetPositionAndRotation(centre, turn);
            ZebraAcrossEW(markingsFrame, zoneFrame, "Tutorial zebra", 0f, hw, k_ZebraWidth);
            DroppedKerbs(markingsFrame, hw);

            // Car: right-hand lane, well before the zebra, facing along the loop.
            var ci = Mathf.RoundToInt(zi - k_CarStartBeforeZebra / step);
            var carPos = HighwayLoopKit.Lateral(loop, ci, hw * 0.5f);
            carPos.y = HighwayLoopKit.SurfaceY(carPos);
            PlayerCar(rig, carPos, Yaw(HighwayLoopKit.Direction(loop, (ci % n + n) % n)));

            // Pedestrian: right-hand pavement, a few metres before the zebra, walking towards it.
            var pi = Mathf.RoundToInt(zi - k_PedestrianStartBeforeZebra / step);
            var pedPos = HighwayLoopKit.Lateral(loop, pi, hw + k_PavementWidth * 0.5f);
            pedPos.y = HighwayLoopKit.SurfaceY(pedPos, SidewalkTop);
            PedestrianSpawn(rig, pedPos, Yaw(HighwayLoopKit.Direction(loop, (pi % n + n) % n)));

            var director = Group(rig.root, "Tutorial Director").gameObject.AddComponent<TutorialDirector>();
            var markerPos = HighwayLoopKit.Lateral(loop, zi, hw + 0.7f);
            markerPos.y = HighwayLoopKit.SurfaceY(markerPos, SidewalkTop);
            director.KerbMarker = Point(rig.spawns, "Kerb marker", markerPos);
            Box(director.KerbMarker, "Marker disc", new Vector3(0f, 0.01f, 0f), new Vector3(0.9f, 0.02f, 0.9f), "marker");

            LoopScenery(rig, loop, zi);
        }

        /// <summary>Carriageway and both pavements as chains of short zones turned to follow the curve.</summary>
        static void LoopZones(Rig rig, HighwayLoopKit.Loop loop)
        {
            var n = loop.centreLine.Count;
            var segment = loop.length / n + 0.4f; // a little overlap so no gap opens on bends
            var hw = loop.meanHalfWidth;
            var road = Group(rig.zones, "Loop carriageway");
            var pavements = Group(rig.zones, "Loop pavements");
            for (var i = 0; i < n; i++)
            {
                var yaw = Yaw(HighwayLoopKit.Direction(loop, i));
                Configure(Point(road, "Carriageway", loop.centreLine[i], yaw), RoadZoneType.Carriageway, new Vector3(hw * 2f, 3f, segment));
                foreach (var side in new[] { 1f, -1f })
                    Configure(Point(pavements, side > 0f ? "Pavement (outer)" : "Pavement (inner)", HighwayLoopKit.Lateral(loop, i, side * (hw + k_PavementWidth * 0.5f)), yaw),
                        RoadZoneType.Sidewalk, new Vector3(k_PavementWidth, 3f, segment));
            }

            static void Configure(Transform t, RoadZoneType type, Vector3 size) => t.gameObject.AddComponent<RoadZone>().Configure(type, size);
        }

        /// <summary>Invisible ramps at both ends of the zebra so walkers can step between the road and the raised pavement.</summary>
        static void DroppedKerbs(Transform frame, float roadHalfWidth)
        {
            const float rise = 0.42f, run = 1.2f;
            var angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            foreach (var side in new[] { 1f, -1f })
            {
                var ramp = Box(frame, side > 0f ? "Dropped kerb (left)" : "Dropped kerb (right)",
                    new Vector3(0f, rise * 0.5f - 0.03f, side * (roadHalfWidth + 0.1f)), new Vector3(k_ZebraWidth, 0.06f, Mathf.Sqrt(rise * rise + run * run)), "marking",
                    collider: true, rotation: Quaternion.Euler(-side * angle, 0f, 0f));
                ramp.GetComponent<MeshRenderer>().enabled = false;
            }
        }

        /// <summary>Street lights on the outer pavement and trees on the grass either side of the road.</summary>
        static void LoopScenery(Rig rig, HighwayLoopKit.Loop loop, int zebraIndex)
        {
            var n = loop.centreLine.Count;
            var hw = loop.meanHalfWidth;
            var lightsEvery = Mathf.Max(1, Mathf.RoundToInt(30f / (loop.length / n)));
            var treesEvery = Mathf.Max(1, Mathf.RoundToInt(18f / (loop.length / n)));
            for (var i = 0; i < n; i++)
            {
                var nearZebra = Mathf.Abs(i - zebraIndex) < 4 || Mathf.Abs(i - zebraIndex) > n - 4;
                if (i % lightsEvery == 0 && !nearZebra && StreetKit.Available)
                {
                    var p = HighwayLoopKit.Lateral(loop, i, hw + k_PavementWidth - 0.5f);
                    p.y = HighwayLoopKit.SurfaceY(p, SidewalkTop);
                    StreetKit.StreetLight(rig.env, p, -(HighwayLoopKit.Lateral(loop, i, 1f) - loop.centreLine[i]));
                }

                if (i % treesEvery == treesEvery / 2 && !nearZebra)
                    foreach (var side in new[] { 1f, -1f })
                    {
                        var t = HighwayLoopKit.Lateral(loop, i + (side > 0f ? 0 : treesEvery / 2), side * (hw + k_PavementWidth + 3f));
                        t.y = -0.1f;
                        Tree(rig.env, t);
                    }
            }
        }
    }
}
