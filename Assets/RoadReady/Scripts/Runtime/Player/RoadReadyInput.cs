using UnityEngine.InputSystem;

namespace RoadReady.Player
{
    /// <summary>
    /// Training-specific input actions (FR3/FR4), defined in code so there is no asset to mis-wire.
    /// Uses XR usage paths (works for Quest Touch and other OpenXR controllers) plus keyboard / mouse
    /// fallbacks for desktop testing. XRI keeps its own actions for UI pointing and walking/turning.
    /// </summary>
    public sealed class RoadReadyInput
    {
        const string k_Left = "<XRController>{LeftHand}";
        const string k_Right = "<XRController>{RightHand}";

        static RoadReadyInput s_Instance;

        public static RoadReadyInput Instance => s_Instance ??= new RoadReadyInput();

        public readonly InputActionMap Driving = new InputActionMap("Driving");
        public readonly InputActionMap Walking = new InputActionMap("Walking");
        public readonly InputActionMap Global = new InputActionMap("Global");

        public readonly InputAction Throttle;
        public readonly InputAction Brake;
        public readonly InputAction Steer;
        public readonly InputAction IndicatorLeft;
        public readonly InputAction IndicatorRight;
        public readonly InputAction Horn;

        public readonly InputAction CrossNow;
        public readonly InputAction StopRetreat;
        public readonly InputAction DesktopMove;
        public readonly InputAction DesktopLook;
        public readonly InputAction DesktopLookHold;

        public readonly InputAction HazardSpotted;
        public readonly InputAction Pause;
        public readonly InputAction Recenter;

        RoadReadyInput()
        {
            Throttle = Driving.AddAction("Throttle", InputActionType.Value, expectedControlLayout: "Axis");
            Throttle.AddBinding(k_Right + "/{Trigger}");
            Throttle.AddBinding("<Keyboard>/w");
            Throttle.AddBinding("<Keyboard>/upArrow");
            Throttle.AddBinding("<Gamepad>/rightTrigger");

            Brake = Driving.AddAction("Brake", InputActionType.Value, expectedControlLayout: "Axis");
            Brake.AddBinding(k_Left + "/{Trigger}");
            Brake.AddBinding("<Keyboard>/s");
            Brake.AddBinding("<Keyboard>/downArrow");
            Brake.AddBinding("<Gamepad>/leftTrigger");

            Steer = Driving.AddAction("Steer", InputActionType.Value, expectedControlLayout: "Axis");
            Steer.AddBinding(k_Left + "/{Primary2DAxis}/x");
            Steer.AddBinding("<Gamepad>/leftStick/x");
            Steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
            Steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/leftArrow").With("Positive", "<Keyboard>/rightArrow");

            IndicatorLeft = Driving.AddAction("IndicatorLeft", InputActionType.Button);
            IndicatorLeft.AddBinding(k_Left + "/{GripButton}");
            IndicatorLeft.AddBinding("<Keyboard>/q");

            IndicatorRight = Driving.AddAction("IndicatorRight", InputActionType.Button);
            IndicatorRight.AddBinding(k_Right + "/{GripButton}");
            IndicatorRight.AddBinding("<Keyboard>/e");

            Horn = Driving.AddAction("Horn", InputActionType.Button);
            Horn.AddBinding(k_Right + "/{SecondaryButton}");
            Horn.AddBinding("<Keyboard>/h");

            CrossNow = Walking.AddAction("CrossNow", InputActionType.Button);
            CrossNow.AddBinding(k_Right + "/{PrimaryButton}");
            CrossNow.AddBinding("<Keyboard>/space");

            StopRetreat = Walking.AddAction("StopRetreat", InputActionType.Button);
            StopRetreat.AddBinding(k_Right + "/{SecondaryButton}");
            StopRetreat.AddBinding("<Keyboard>/c");

            DesktopMove = Walking.AddAction("DesktopMove", InputActionType.Value, expectedControlLayout: "Vector2");
            DesktopMove.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");

            DesktopLook = Walking.AddAction("DesktopLook", InputActionType.Value, expectedControlLayout: "Vector2");
            DesktopLook.AddBinding("<Mouse>/delta");

            DesktopLookHold = Walking.AddAction("DesktopLookHold", InputActionType.Button);
            DesktopLookHold.AddBinding("<Mouse>/rightButton");

            HazardSpotted = Global.AddAction("HazardSpotted", InputActionType.Button);
            HazardSpotted.AddBinding(k_Left + "/{PrimaryButton}");
            HazardSpotted.AddBinding("<Keyboard>/f");

            Pause = Global.AddAction("Pause", InputActionType.Button);
            Pause.AddBinding(k_Left + "/{MenuButton}");
            Pause.AddBinding("<Keyboard>/escape");

            Recenter = Global.AddAction("Recenter", InputActionType.Button);
            Recenter.AddBinding(k_Right + "/{Primary2DAxisClick}");
            Recenter.AddBinding("<Keyboard>/r");

            Global.Enable();
        }

        public void SetDriving(bool enabled)
        {
            if (enabled) Driving.Enable();
            else Driving.Disable();
        }

        public void SetWalking(bool enabled)
        {
            if (enabled) Walking.Enable();
            else Walking.Disable();
        }
    }
}
