using System.Collections.Generic;
using RoadReady.Core;
using UnityEngine;

namespace RoadReady.Traffic
{
    /// <summary>
    /// Where a driver must check a blind spot / mirror before a manoeuvre - typically where moto-taxis
    /// filter up the inside before a turn. Evaluated by the BlindSpotCheckRule using head yaw.
    /// </summary>
    public class BlindSpotCheckZone : BoxVolume
    {
        static readonly List<BlindSpotCheckZone> s_Zones = new List<BlindSpotCheckZone>();

        [SerializeField] TurnSide m_Side = TurnSide.Right;
        [SerializeField] string m_Title = "Blind-spot check before turning";

        public static IReadOnlyList<BlindSpotCheckZone> All => s_Zones;
        public TurnSide Side { get => m_Side; set => m_Side = value; }
        public string Title { get => m_Title; set => m_Title = value; }
        public string ZoneId => name;

        void OnEnable() => s_Zones.Add(this);

        void OnDisable() => s_Zones.Remove(this);

        void OnDrawGizmos() => DrawBox(new Color(1f, 0.9f, 0.1f, 0.8f));
    }

}
