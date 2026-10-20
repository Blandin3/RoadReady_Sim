using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace RoadReady.Player
{
    /// <summary>
    /// Flat-screen driving (no headset, e.g. a G29 rig in front of a monitor): hold the right mouse button and move
    /// the mouse to look around the cabin and check mirrors; V snaps the view back to straight ahead. Does nothing
    /// while a headset or the XR Interaction Simulator drives the camera. (Walking has its own desktop look.)
    /// </summary>
    public class FlatScreenLook : MonoBehaviour
    {
        const float k_Sensitivity = 0.12f;
        const float k_MaxYaw = 150f;
        const float k_MaxPitch = 60f;

        float m_Yaw, m_Pitch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (FindAnyObjectByType<FlatScreenLook>() != null)
                return;
            var go = new GameObject("Flat Screen Look");
            DontDestroyOnLoad(go);
            go.AddComponent<FlatScreenLook>();
        }

        static bool HeadTracked => XRSettings.isDeviceActive || SimulatorHeadsetGuard.SimulatorOn;

        void LateUpdate()
        {
            var app = RoadReadyApp.Instance;
            var mouse = Mouse.current;
            if (app == null || app.Rig == null || !app.Rig.IsSeated || HeadTracked)
            {
                m_Yaw = m_Pitch = 0f;
                return;
            }

            if (mouse != null && mouse.rightButton.isPressed)
            {
                var delta = mouse.delta.ReadValue() * k_Sensitivity;
                m_Yaw = Mathf.Clamp(m_Yaw + delta.x, -k_MaxYaw, k_MaxYaw);
                m_Pitch = Mathf.Clamp(m_Pitch - delta.y, -k_MaxPitch, k_MaxPitch);
            }

            if (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
                m_Yaw = m_Pitch = 0f;

            app.Rig.HeadTransform.localRotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
        }
    }
}
