using UnityEngine;
using RoadReady.Core;

namespace RoadReady.Player
{
    /// <summary>
    /// Lights the engaged gear on the car's own shifter: a yellow glowing letter sits on the engraved P, R, N or D of the
    /// console and only the one for the current gear is shown.
    /// </summary>
    public class GearIndicator : MonoBehaviour
    {
        [SerializeField] DriverController m_Driver;
        [Tooltip("One marker per gear, indexed by the Gear enum (P, R, N, D).")]
        [SerializeField] GameObject[] m_Markers = System.Array.Empty<GameObject>();

        public void Configure(DriverController driver, GameObject[] markers)
        {
            m_Driver = driver;
            m_Markers = markers;
        }

        void OnEnable()
        {
            if (m_Driver == null)
                return;
            m_Driver.GearChanged += Show;
            Show(m_Driver.Gear);
        }

        void OnDisable()
        {
            if (m_Driver != null)
                m_Driver.GearChanged -= Show;
        }

        void Show(Gear gear)
        {
            for (var i = 0; i < m_Markers.Length; i++)
                if (m_Markers[i] != null)
                    m_Markers[i].SetActive(i == (int)gear);
        }
    }
}
