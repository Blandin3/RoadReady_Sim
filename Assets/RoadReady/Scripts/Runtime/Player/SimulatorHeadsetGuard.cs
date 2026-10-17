using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace RoadReady.Player
{
    /// <summary>
    /// Sits on the XR Interaction Simulator in the bootstrap scene (tagged EditorOnly, so never in a build).
    /// When a real headset is running (e.g. a Quest over Link) the simulator is switched off so it cannot fight the
    /// real controllers. With no headset it starts on, so menus and controller grabs can be tried with keyboard and
    /// mouse. F1 toggles it: the simulator moves the headset with W/A/S/D/Q/E and the arrow keys, which are also
    /// RoadReady's keyboard driving keys, so switch it off (F1) to drive with the keyboard and on again for menus.
    /// </summary>
    public class SimulatorHeadsetGuard : MonoBehaviour
    {
        readonly List<Behaviour> m_Simulator = new List<Behaviour>();
        readonly List<GameObject> m_Children = new List<GameObject>();
        bool m_On = true;

        void Awake()
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays)
            {
                if (!display.running)
                    continue;
                Debug.Log("[RoadReady] Headset detected: XR Interaction Simulator disabled.");
                gameObject.SetActive(false);
                return;
            }

            foreach (var behaviour in GetComponents<Behaviour>())
                if (behaviour != this)
                    m_Simulator.Add(behaviour);
            foreach (Transform child in transform)
                m_Children.Add(child.gameObject);
            Debug.Log("[RoadReady] No headset: XR Interaction Simulator on. Press F1 to switch it off for keyboard driving (and on again for menus).");
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.f1Key.wasPressedThisFrame)
                return;
            m_On = !m_On;
            foreach (var behaviour in m_Simulator)
                if (behaviour != null) behaviour.enabled = m_On;
            foreach (var child in m_Children)
                if (child != null) child.SetActive(m_On);
            Debug.Log($"[RoadReady] XR Interaction Simulator {(m_On ? "on (menus and controllers)" : "off (keyboard driving)")}.");
        }
    }
}
