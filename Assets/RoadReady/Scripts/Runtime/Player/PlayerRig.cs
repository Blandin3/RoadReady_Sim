using System.Collections.Generic;
using RoadReady.Config;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace RoadReady.Player
{
    /// <summary>
    /// Owns the XRI "XR Origin (XR Rig)": seats it in the car for the driver module, stands it on the kerb
    /// for the pedestrian module, and toggles locomotion, pointers and comfort options accordingly.
    /// </summary>
    public class PlayerRig : MonoBehaviour
    {
        [SerializeField] XROrigin m_Origin;
        [Tooltip("Where the rig waits while menus are shown between scenarios.")]
        [SerializeField] Transform m_LobbySpawn;

        readonly List<LocomotionProvider> m_Providers = new List<LocomotionProvider>();
        readonly List<GameObject> m_PointerObjects = new List<GameObject>();
        CharacterController m_CharacterController;
        ControllerInputActionManager[] m_InputManagers;
        Transform m_Seat;
        ComfortSettings m_Comfort;
        float m_DefaultCameraYOffset;
        XROrigin.TrackingOriginMode m_DefaultTrackingMode;
        Scene m_HomeScene;

        public XROrigin Origin => m_Origin;
        public Camera Head => m_Origin != null ? m_Origin.Camera : Camera.main;
        public Transform HeadTransform => Head != null ? Head.transform : transform;
        public TunnelingVignetteController Vignette { get; private set; }
        public bool IsSeated => m_Seat != null;
        public Transform LobbySpawn { get => m_LobbySpawn; set => m_LobbySpawn = value; }

        public void Initialize(ComfortSettings comfort)
        {
            if (m_Origin == null)
                m_Origin = FindAnyObjectByType<XROrigin>();
            if (m_Origin == null)
            {
                Debug.LogError("[RoadReady] No XR Origin found in the bootstrap scene.");
                return;
            }

            m_CharacterController = m_Origin.GetComponent<CharacterController>();
            m_InputManagers = m_Origin.GetComponentsInChildren<ControllerInputActionManager>(true);
            m_Providers.AddRange(m_Origin.GetComponentsInChildren<LocomotionProvider>(true));
            foreach (var interactor in m_Origin.GetComponentsInChildren<NearFarInteractor>(true))
                m_PointerObjects.Add(interactor.gameObject);
            foreach (var interactor in m_Origin.GetComponentsInChildren<XRRayInteractor>(true))
                m_PointerObjects.Add(interactor.gameObject);
            Vignette = m_Origin.GetComponentInChildren<TunnelingVignetteController>(true);
            m_DefaultCameraYOffset = m_Origin.CameraYOffset;
            m_DefaultTrackingMode = m_Origin.RequestedTrackingOriginMode;
            m_HomeScene = m_Origin.gameObject.scene;

            // Smooth-motion mode keeps the left stick from triggering the teleport ray: teleporting
            // across a road would skip the very decision we are training.
            foreach (var manager in m_InputManagers)
                manager.smoothMotionEnabled = true;

            ApplyComfort(comfort);
            SetLocomotion(false);
        }

        public void ApplyComfort(ComfortSettings comfort)
        {
            m_Comfort = comfort;
            foreach (var manager in m_InputManagers ?? System.Array.Empty<ControllerInputActionManager>())
                manager.smoothTurnEnabled = comfort.turnMode == TurnMode.Smooth;
            foreach (var provider in m_Providers)
            {
                if (provider is SnapTurnProvider snap)
                    snap.turnAmount = comfort.snapTurnDegrees;
                if (provider is ContinuousMoveProvider move)
                    move.moveSpeed = comfort.walkSpeed;
            }

            if (Vignette != null)
                Vignette.defaultParameters.apertureSize = Mathf.Lerp(0.95f, 0.45f, comfort.vignetteStrength);
        }

        /// <summary>Pedestrian module: thumbstick walking + turning, never teleport / climb / jump.</summary>
        public void SetLocomotion(bool walking)
        {
            foreach (var provider in m_Providers)
            {
                var allowed = walking && (
                    (provider is ContinuousMoveProvider && m_Comfort.freeWalking) ||
                    provider is GravityProvider ||
                    (provider is SnapTurnProvider && m_Comfort.turnMode == TurnMode.Snap) ||
                    (provider is ContinuousTurnProvider && m_Comfort.turnMode == TurnMode.Smooth));
                provider.enabled = allowed;
            }
        }

        /// <summary>Show controller rays only while a menu needs pointing.</summary>
        public void SetPointers(bool visible)
        {
            foreach (var go in m_PointerObjects)
                if (go != null) go.SetActive(visible);
        }

        /// <summary>Driver module: parent the rig to the car and put the head at the seat eye point.</summary>
        public void AttachToSeat(Transform eyePoint, Transform vehicle)
        {
            if (m_Origin == null || eyePoint == null)
                return;
            m_Seat = eyePoint;
            SetCharacterController(false);
            SetStandingMode(false);
            m_Origin.transform.SetParent(vehicle, true);
            Recenter();
        }

        /// <summary>Pedestrian module / lobby: stand the rig at a world position, keeping real head height.</summary>
        public void PlaceStanding(Transform spawn)
        {
            if (m_Origin == null || spawn == null)
                return;
            m_Seat = null;
            SetCharacterController(false);
            Detach();
            SetStandingMode(m_Comfort != null && m_Comfort.seatedMode);

            var originTransform = m_Origin.transform;
            originTransform.rotation = Quaternion.identity;
            m_Origin.MatchOriginUpCameraForward(Vector3.up, Flat(spawn.forward));
            var head = HeadTransform.position;
            var offset = new Vector3(spawn.position.x - head.x, 0f, spawn.position.z - head.z);
            originTransform.position = new Vector3(originTransform.position.x + offset.x, spawn.position.y, originTransform.position.z + offset.z);
            SetCharacterController(true);
        }

        public void ReturnToLobby()
        {
            if (m_LobbySpawn != null)
            {
                PlaceStanding(m_LobbySpawn);
                return;
            }

            m_Seat = null;
            Detach();
            SetCharacterController(true);
        }

        /// <summary>
        /// Un-parents the rig and moves it back to the bootstrap scene. Without this, a rig that was seated in a
        /// scenario car becomes a root object of the scenario scene and is destroyed when that scene unloads.
        /// </summary>
        void Detach()
        {
            if (m_Origin == null)
                return;
            m_Origin.transform.SetParent(null, true);
            if (m_HomeScene.IsValid() && m_Origin.gameObject.scene != m_HomeScene)
                SceneManager.MoveGameObjectToScene(m_Origin.gameObject, m_HomeScene);
        }

        /// <summary>Re-align the view with the seat (driver) or current facing (pedestrian).</summary>
        public void Recenter()
        {
            if (m_Origin == null)
                return;
            if (m_Seat != null)
            {
                m_Origin.MatchOriginUpCameraForward(m_Seat.up, m_Seat.forward);
                m_Origin.MoveCameraToWorldLocation(m_Seat.position);
            }
        }

        /// <summary>Moves the standing rig horizontally (assisted crossing / desktop walking).</summary>
        public void MoveHorizontal(Vector3 delta)
        {
            if (m_Origin == null)
                return;
            delta.y = 0f;
            m_Origin.transform.position += delta;
        }

        public void RotateYaw(float degrees)
        {
            if (m_Origin != null)
                m_Origin.RotateAroundCameraUsingOriginUp(degrees);
        }

        void SetStandingMode(bool seatedPedestrian)
        {
            if (seatedPedestrian)
            {
                m_Origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
                m_Origin.CameraYOffset = m_Comfort.seatedEyeHeight;
            }
            else
            {
                m_Origin.RequestedTrackingOriginMode = m_DefaultTrackingMode;
                m_Origin.CameraYOffset = m_DefaultCameraYOffset;
            }
        }

        void SetCharacterController(bool enabled)
        {
            if (m_CharacterController != null)
                m_CharacterController.enabled = enabled;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward;
        }
    }
}
