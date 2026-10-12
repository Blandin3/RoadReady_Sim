using RoadReady.Core;
using RoadReady.Player;
using RoadReady.Traffic;
using UnityEditor;
using UnityEngine;
using static RoadReady.EditorTools.GreyboxKit;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// RoadReady > Set Up Vehicle Audio: wires the "Vehicle Essentials" clips into the player car (engine, road noise,
    /// horn, indicators, gear lever, start/stop) and the traffic prefabs (3D engine loops). Safe to run again.
    /// </summary>
    public static class VehicleAudioSetup
    {
        const string Car = "Assets/Audios/Vehicle_Essentials/Vehicle_Car/";
        const string Van = "Assets/Audios/Vehicle_Essentials/Vehicle_Van/";

        static AudioClip Clip(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
                Debug.LogWarning($"[RoadReady] Audio clip not found: {path}");
            return clip;
        }

        public static bool Available => AssetDatabase.IsValidFolder("Assets/Audios/Vehicle_Essentials");

        [MenuItem("RoadReady/Set Up Vehicle Audio", priority = 31)]
        public static void Run()
        {
            if (!Available)
            {
                Debug.LogWarning("[RoadReady] Vehicle Essentials audio not found under Assets/Audios - vehicle sounds skipped.");
                return;
            }

            SetUpPlayerCar();
            SetUpTraffic(RoadUserKind.Car, Car + "Vehicle_Car_Engine/Vehicle_Car_Engine_Idle_Exterior_Loop_01.wav", Car + "Vehicle_Car_Drive/Vehicle_Car_Drive_Exterior_Short_Loop.wav", 1f, 0.7f);
            SetUpTraffic(RoadUserKind.Bus, Van + "Vehicle_Van_Engine/Vehicle_Van_Idle_Exterior_Front_Loop_01.wav", Van + "Vehicle_Van_Drive/Vehicle_Van_Drive_Exterior_Loop_01.wav", 0.8f, 0.85f);
            // No motorbike in the pack: a smaller, higher-revving car engine stands in for the moto-taxis.
            SetUpTraffic(RoadUserKind.MotoTaxi, Car + "Vehicle_Car_Engine/Vehicle_Car_Engine_Idle_Exterior_Loop_02.wav", Car + "Vehicle_Car_Engine/Vehicle_Car_Engine_2000_RPM_Front_Exterior_Loop.wav", 1.6f, 0.5f);
            AssetDatabase.SaveAssets();
            Debug.Log("[RoadReady] Vehicle audio set up on the player car and traffic (car, bus, moto-taxi).");
        }

        static void SetUpPlayerCar()
        {
            var path = PrefabsFolder + "/PlayerCar.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var audio = root.GetComponent<PlayerCarAudio>();
                if (audio == null)
                    audio = root.AddComponent<PlayerCarAudio>();
                audio.Configure(
                    doorClose: Clip(Car + "Vehicle_Car_Doors/Vehicle_Car_Door_Closing_Interior_01.wav"),
                    seatBelt: Clip(Car + "Vehicle_Car_Seat_Belt/Vehicle_Car_Seat_Belt_01.wav"),
                    keys: Clip(Car + "Vehicle_Car_Keys/Vehicle_Car_Keys_01.wav"),
                    engineStart: Clip(Car + "Vehicle_Car_Engine/Vehicle_Car_Start_Engine_Exterior.wav"),
                    engineStop: Clip(Car + "Vehicle_Car_Engine/Vehicle_Car_Stop_Engine_Exterior.wav"),
                    idle: Clip(Car + "Vehicle_Car_Engine/Vehicle_Car_Engine_Idle_Interior_Loop_01.wav"),
                    rpm1000: Clip(Car + "Vehicle_Car_Engine/Vehicle_Car_Engine_1000_RPM_Front_Exterior_Loop.wav"),
                    rpm2000: Clip(Car + "Vehicle_Car_Engine/Vehicle_Car_Engine_2000_RPM_Front_Exterior_Loop.wav"),
                    roadNoise: Clip(Car + "Vehicle_Car_Drive/Vehicle_Car_Drive_Interior_Loop_01.wav"),
                    horn: Clip(Car + "Vehicle_Car_Horns/Vehicle_Car_Horn_Interior.wav"),
                    // The pack has no indicator relay; two short dashboard button clicks make the tick-tock.
                    tick: Clip(Car + "Vehicle_Car_Buttons/Vehicle_Car_Button_01.wav"),
                    tock: Clip(Car + "Vehicle_Car_Buttons/Vehicle_Car_Button_02.wav"),
                    gearShift: Clip(Car + "Vehicle_Car_Buttons/Vehicle_Car_Button_Movement_01.wav"),
                    handbrake: Clip(Car + "Vehicle_Car_Handbrake/Vehicle_Car_HandBrake_01.wav"),
                    // No crash sound in the pack: the boot-lid slam is the closest heavy impact.
                    impact: Clip(Car + "Vehicle_Car_Trunk/Vehicle_Car_Trunk_Close_Impact.wav"));
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void SetUpTraffic(RoadUserKind kind, string idlePath, string drivePath, float pitch, float volume)
        {
            var path = $"{PrefabsFolder}/NPC_{kind}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                return;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var audio = root.GetComponent<VehicleEngineAudio>();
                if (audio == null)
                    audio = root.AddComponent<VehicleEngineAudio>();
                audio.Configure(Clip(idlePath), Clip(drivePath), pitch, volume);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
