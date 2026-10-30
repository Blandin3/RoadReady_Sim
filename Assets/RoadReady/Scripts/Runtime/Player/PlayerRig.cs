using System.Collections.Generic;
using RoadReady.Config;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
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
        [Header("Driving hands (XR Hands sample models)")]
        [SerializeField] GameObject m_LeftHandModel;
        [SerializeField] GameObject m_RightHandModel;
        [SerializeField] Material m_HandMaterial;

        readonly List<LocomotionProvider> m_Providers = new List<LocomotionProvider>();
        readonly List<GameObject> m_PointerObjects = new List<GameObject>();
        CharacterController m_CharacterController;
        ControllerInputActionManager[] m_InputManagers;
        Transform m_Seat;
        ComfortSettings m_Comfort;
        float m_DefaultCameraYOffset;
        XROrigin.TrackingOriginMode m_DefaultTrackingMode;
        Scene m_HomeScene;
        // Where the driver sits relative to the eye point (metres: x right, z forward), moved with the right thumbstick.
        const string k_SeatPrefs = "RoadReady.Seat.";
        const float k_SeatSpeed = 0.25f;
        Vector3 m_SeatOffset;
        bool m_SeatDirty;
        bool m_HeadsetWorn = true;
        bool m_TrackingReset;
        int m_RecenterFrames;
        readonly List<NearFarInteractor> m_NearFar = new List<NearFarInteractor>();
        DriverHands m_Hands;
        bool m_PointersVisible = true;
        bool m_Driving;

        public XROrigin Origin => m_Origin;

        public void SetHandModels(GameObject left, GameObject right, Material material)
        {
            m_LeftHandModel = left;
            m_RightHandModel = right;
            m_HandMaterial = material;
        }
        public Camera Head => m_Origin != null ? m_Origin.Camera : Camera.main;
        public Transform HeadTransform => Head != null ? Head.transform : transform;
        public TunnelingVignetteController Vignette { get; private set; }
        public bool IsSeated => m_Seat != null;
        /// <summary>The driver's eye point while seated in the car, else null.</summary>
        public Transform Seat => m_Seat;
        /// <summary>Raised when the view re-centred by itself (headset put back on, tracking origin reset).</summary>
        public event System.Action AutoRecentered;
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
            {
                m_PointerObjects.Add(interactor.gameObject);
                m_NearFar.Add(interactor);
            }

            m_Hands = m_Origin.gameObject.AddComponent<DriverHands>();
            m_Hands.Setup(FindChild(m_Origin.transform, "Left Controller"), FindChild(m_Origin.transform, "Right Controller"),
                m_LeftHandModel, m_RightHandModel, m_HandMaterial);
            foreach (var interactor in m_Origin.GetComponentsInChildren<XRRayInteractor>(true))
            {
                // The starter rig also carries a plain Ray and a Teleport interactor on each controller. Showing them
                // next to the Near-Far one gives two rays (one white, one red) that fight over the same buttons, and
                // teleporting is never used here. So the Near-Far interactor is the only pointer.
                if (m_NearFar.Count > 0)
                    interactor.gameObject.SetActive(false);
                else
                    m_PointerObjects.Add(interactor.gameObject);
            }
            Vignette = m_Origin.GetComponentInChildren<TunnelingVignetteController>(true);
            m_DefaultCameraYOffset = m_Origin.CameraYOffset;
            m_DefaultTrackingMode = m_Origin.RequestedTrackingOriginMode;
            m_HomeScene = m_Origin.gameObject.scene;
            m_SeatOffset = new Vector3(PlayerPrefs.GetFloat(k_SeatPrefs + "x"), 0f, PlayerPrefs.GetFloat(k_SeatPrefs + "z"));

            // Smooth-motion mode keeps the left stick from triggering the teleport ray: teleporting
            // across a road would skip the very decision we are training.
            foreach (var manager in m_InputManagers)
                manager.smoothMotionEnabled = true;

            ApplyComfort(comfort);
            SetLocomotion(false);
        }

        void OnEnable()
        {
            var inputs = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(inputs);
            foreach (var input in inputs)
                input.trackingOriginUpdated += OnTrackingOriginUpdated;
        }

        void OnDisable()
        {
            var inputs = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(inputs);
            foreach (var input in inputs)
                input.trackingOriginUpdated -= OnTrackingOriginUpdated;
        }

        void OnTrackingOriginUpdated(XRInputSubsystem subsystem) => m_TrackingReset = true;

        /// <summary>
        /// Taking the headset off (or the runtime resetting its tracking space) leaves the driver looking away from the
        /// road. Put the view back on the seat once the headset is worn again and its tracking has settled.
        /// </summary>
        void Update()
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            var worn = !head.isValid || !head.TryGetFeatureValue(CommonUsages.userPresence, out var present) || present;
            if (worn && !m_HeadsetWorn)
                m_RecenterFrames = 30;
            m_HeadsetWorn = worn;

            if (m_TrackingReset)
            {
                m_TrackingReset = false;
                m_RecenterFrames = Mathf.Max(m_RecenterFrames, 10);
            }

            if (m_RecenterFrames > 0 && --m_RecenterFrames == 0)
            {
                Recenter();
                AutoRecentered?.Invoke();
            }
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

        /// <summary>
        /// Show controller rays only while a menu needs pointing. While driving the grab interactors stay on (near
        /// grabbing only) so the hands can hold the steering wheel and gear lever.
        /// </summary>
        public bool PointersVisible => m_PointersVisible;

        public void SetPointers(bool visible)
        {
            m_PointersVisible = visible;
            foreach (var go in m_PointerObjects)
                if (go != null) go.SetActive(visible || m_Driving);
            foreach (var interactor in m_NearFar)
                if (interactor != null) interactor.enableFarCasting = visible;
        }

        /// <summary>Driving hands on/off: hands replace the controller models and near grabbing stays active.</summary>
        void SetDriving(bool driving)
        {
            m_Driving = driving;
            if (m_Hands != null) m_Hands.Show(driving);
            SetPointers(m_PointersVisible);
        }

        static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
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
            SetDriving(true);
        }

        /// <summary>Pedestrian module / lobby: stand the rig at a world position, keeping real head height.</summary>
        public void PlaceStanding(Transform spawn)
        {
            if (m_Origin == null || spawn == null)
                return;
            SetDriving(false);
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
            SetDriving(false);
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
                m_Origin.MoveCameraToWorldLocation(m_Seat.TransformPoint(m_SeatOffset));
            }
        }

        /// <summary>
        /// Seat adjustment: the right thumbstick slides the driver's head forwards / back and left / right inside the
        /// car. The position is remembered between drives. Does nothing outside the car.
        /// </summary>
        public void AdjustSeat(Vector2 stick, float dt)
        {
            if (m_Origin == null || m_Seat == null)
                return;
            if (stick.sqrMagnitude < 0.09f)
            {
                if (m_SeatDirty)
                {
                    m_SeatDirty = false;
                    PlayerPrefs.SetFloat(k_SeatPrefs + "x", m_SeatOffset.x);
                    PlayerPrefs.SetFloat(k_SeatPrefs + "z", m_SeatOffset.z);
                    PlayerPrefs.Save();
                }

                return;
            }

            // Forward / back (closeness to the wheel) is the main use: sideways only when the stick is clearly pushed sideways.
            if (Mathf.Abs(stick.x) < Mathf.Abs(stick.y) * 1.5f)
                stick.x = 0f;
            m_SeatOffset.x = Mathf.Clamp(m_SeatOffset.x + stick.x * k_SeatSpeed * dt, -0.30f, 0.30f);
            m_SeatOffset.z = Mathf.Clamp(m_SeatOffset.z + stick.y * k_SeatSpeed * dt, -0.50f, 0.40f);
            m_SeatDirty = true;
            m_Origin.MoveCameraToWorldLocation(m_Seat.TransformPoint(m_SeatOffset));
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
