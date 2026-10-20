using System.Collections.Generic;
using System.Linq;
using RoadReady.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UnityEngine.XR;

namespace RoadReady.UI
{
    /// <summary>
    /// Flat-screen use (no headset, e.g. a driving rig at a monitor): a left mouse click on a world-space menu panel
    /// presses the button / toggle under the cursor. The panels are normally pointed at with XR controller rays, which
    /// do not exist without a headset. Inactive while a headset or the XR Interaction Simulator is running.
    /// </summary>
    public class FlatScreenMenus : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (FindAnyObjectByType<FlatScreenMenus>() != null)
                return;
            var go = new GameObject("Flat Screen Menus");
            DontDestroyOnLoad(go);
            go.AddComponent<FlatScreenMenus>();
        }

        static bool XrActive => XRSettings.isDeviceActive || SimulatorHeadsetGuard.SimulatorOn;

        void Update()
        {
            var mouse = Mouse.current;
            var app = RoadReadyApp.Instance;
            if (mouse != null && app != null && !XrActive && mouse.leftButton.wasPressedThisFrame)
                ClickAt(mouse.position.ReadValue());
        }

        /// <summary>Presses the menu button / toggle under a screen position. Returns true if something was pressed.</summary>
        public static bool ClickAt(Vector2 screenPosition)
        {
            var app = RoadReadyApp.Instance;
            if (app == null)
                return false;
            var camera = app.Rig != null ? app.Rig.Head.GetComponent<Camera>() : Camera.main;
            if (camera == null)
                return false;

            // Nearest panel first; skip panels with nothing pressable under the cursor (e.g. the full-screen fader).
            var ray = camera.ScreenPointToRay(screenPosition);
            var hits = new List<(float distance, WorldSpacePanel panel, Vector3 point)>();
            foreach (var panel in FindObjectsByType<WorldSpacePanel>(FindObjectsSortMode.None))
                if (panel.Visible && panel.TryGetComponent<Collider>(out var collider) && collider.enabled && collider.Raycast(ray, out var hit, 20f))
                    hits.Add((hit.distance, panel, hit.point));

            foreach (var (_, panel, point) in hits.OrderBy(h => h.distance))
            {
                // Panel coordinates have their origin at the panel centre, y pointing down.
                var local = panel.transform.InverseTransformPoint(point);
                var picked = panel.Root.panel?.Pick(new Vector2(local.x, -local.y));
                for (var element = picked; element != null; element = element.parent)
                {
                    if (!(element is Button) && !(element is Toggle))
                        continue;
                    if (!element.enabledInHierarchy)
                        return false;
                    using (var submit = NavigationSubmitEvent.GetPooled())
                    {
                        submit.target = element;
                        element.SendEvent(submit);
                    }

                    return true;
                }
            }

            return false;
        }
    }
}
