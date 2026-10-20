using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace RoadReady.Player
{
    /// <summary>
    /// Steering wheel + pedals (Logitech G29 and similar USB wheels) through the Input System, with or without a
    /// headset. Wheels report their axes differently (and pedals rest at +1 or -1 depending on the driver), so the
    /// axes are learned once with a short calibration (F3) and saved; before that, G29 defaults are used and each
    /// pedal's travel is learned from its rest position. F2 shows a live monitor of every axis and button.
    /// </summary>
    public class RacingWheel : MonoBehaviour
    {
        const string k_Prefs = "RoadReady.Wheel.";
        const float k_Deadzone = 0.02f;

        static RacingWheel s_Instance;

        /// <summary>
        /// One pedal axis. After calibration the saved rest / travel are used. Before that the pedal's range is learned
        /// from use: of the two ends seen so far, the one it spends most time at is "released" (pedals mostly rest),
        /// so it does not matter which way the wheel reports them or what it sends before its first report.
        /// </summary>
        class Pedal
        {
            public AxisControl axis;
            public float rest = float.NaN;
            public float travel; // signed rest -> fully pressed; 0 = not calibrated
            readonly float[] m_TimeAt = new float[21]; // time spent at each tenth of the -1..1 range
            float m_Min = float.MaxValue, m_Max = float.MinValue;

            public void Reset()
            {
                System.Array.Clear(m_TimeAt, 0, m_TimeAt.Length);
                m_Min = float.MaxValue;
                m_Max = float.MinValue;
            }

            public float Read()
            {
                if (axis == null) return 0f;
                var v = axis.ReadValue();
                if (!float.IsNaN(rest) && Mathf.Abs(travel) > 0.2f)
                    return Mathf.Clamp01((v - rest) / travel);

                m_Min = Mathf.Min(m_Min, v);
                m_Max = Mathf.Max(m_Max, v);
                m_TimeAt[Mathf.Clamp(Mathf.RoundToInt((v + 1f) * 10f), 0, 20)] += Time.unscaledDeltaTime;
                var restBin = 0;
                for (var i = 1; i < m_TimeAt.Length; i++)
                    if (m_TimeAt[i] > m_TimeAt[restBin]) restBin = i;
                var restValue = restBin / 10f - 1f;
                var pressedEnd = Mathf.Abs(m_Max - restValue) > Mathf.Abs(m_Min - restValue) ? m_Max : m_Min;
                if (Mathf.Abs(pressedEnd - restValue) < 0.3f)
                    return 0f; // not pressed far yet: nothing to scale against
                return Mathf.Clamp01((v - restValue) / (pressedEnd - restValue));
            }
        }

        InputDevice m_Device;
        AxisControl m_Steering;
        float m_SteeringCenter, m_SteeringSign = 1f;
        readonly Pedal m_Throttle = new Pedal();
        readonly Pedal m_Brake = new Pedal();
        bool m_ShowMonitor;
        Calibration m_Calibration;

        /// <summary>The connected wheel, or null.</summary>
        public static RacingWheel Current => s_Instance != null && s_Instance.m_Device != null && s_Instance.m_Steering != null ? s_Instance : null;

        public float Steer { get; private set; }
        public float Throttle { get; private set; }
        public float Brake { get; private set; }
        public string DeviceName => m_Device != null ? m_Device.description.product : "";
        public bool Calibrating => m_Calibration != null;

        // G29 button names as the Input System reports them (generic HID: button1 = first button).
        public const string HornButton = "button1";       // cross
        public const string HandbrakeButton = "button2";  // square
        public const string HazardButton = "button3";     // circle
        public const string RecenterButton = "button4";   // triangle
        public const string RightPaddle = "button5";
        public const string LeftPaddle = "button6";
        public const string PauseButton = "button10";     // options
        public const string GearTowardsDrive = "hat/down"; // D-pad down: P, R, N, D
        public const string GearTowardsPark = "hat/up";

        // Driving Force Shifter (H-pattern, plugged into the G29). Standard G29 numbering: gears 1-6 = button13..button18,
        // reverse = button19. The F3 calibration learns the real numbers (they differ between firmware / modes).
        int m_ShifterFirst = 13;
        int m_ShifterReverse = 19;

        /// <summary>
        /// The H-shifter slot entered this frame: 1-6 for a gear, 0 for reverse, -1 when it was moved back to neutral.
        /// Null when the shifter did not move (or is not connected).
        /// </summary>
        public static int? ShifterMoved()
        {
            var wheel = Current;
            if (wheel == null || wheel.Calibrating)
                return null;
            var anyHeld = false;
            var anyReleased = false;
            for (var gear = 0; gear <= 6; gear++)
            {
                var number = gear == 0 ? wheel.m_ShifterReverse : wheel.m_ShifterFirst + gear - 1;
                var slot = wheel.m_Device.TryGetChildControl<ButtonControl>("button" + number);
                if (slot == null)
                    continue;
                if (slot.wasPressedThisFrame)
                    return gear;
                anyHeld |= slot.isPressed;
                anyReleased |= slot.wasReleasedThisFrame;
            }

            return anyReleased && !anyHeld ? -1 : (int?)null;
        }

        /// <summary>True on the frame a wheel button (by Input System name, e.g. "button2" or "hat/down") goes down.</summary>
        public static bool Pressed(string button)
        {
            var wheel = Current;
            if (wheel == null || wheel.Calibrating)
                return false;
            var control = wheel.m_Device.TryGetChildControl<ButtonControl>(button);
            return control != null && control.wasPressedThisFrame;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (s_Instance != null)
                return;
            var go = new GameObject("Racing Wheel");
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<RacingWheel>();
        }

        void OnEnable()
        {
            InputSystem.onDeviceChange += OnDeviceChange;
            FindDevice();
        }

        void OnDisable() => InputSystem.onDeviceChange -= OnDeviceChange;

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Removed || change == InputDeviceChange.Reconnected || change == InputDeviceChange.Disconnected)
                FindDevice();
        }

        /// <summary>Picks a wheel: a joystick-like device whose name says wheel / G29 / Driving Force, else any joystick.</summary>
        void FindDevice()
        {
            var candidates = InputSystem.devices.Where(d => d.added && !(d is Gamepad) && !(d is Keyboard) && !(d is Mouse) && !(d is Pointer)
                                                            && Axes(d).Any() && (d is Joystick || IsWheelName(d))).ToList();
            var device = candidates.FirstOrDefault(IsWheelName) ?? candidates.FirstOrDefault();
            if (device == m_Device)
                return;
            m_Device = device;
            m_Steering = null;
            m_Throttle.axis = m_Brake.axis = null;
            m_Throttle.Reset();
            m_Brake.Reset();
            m_Throttle.rest = m_Brake.rest = float.NaN;
            m_Throttle.travel = m_Brake.travel = 0f;
            if (device == null)
                return;
            Debug.Log($"[RoadReady] Wheel connected: {DeviceName} (F2: monitor, F3: calibrate).");
            ApplyMapping();
        }

        static bool IsWheelName(InputDevice d)
        {
            var name = (d.description.product + " " + d.displayName).ToLowerInvariant();
            return name.Contains("wheel") || name.Contains("g29") || name.Contains("g920") || name.Contains("g923") || name.Contains("driving force");
        }

        /// <summary>Real analogue axes only (buttons are AxisControls too in the Input System).</summary>
        static IEnumerable<AxisControl> Axes(InputDevice device) =>
            device.allControls.OfType<AxisControl>().Where(a => !(a is ButtonControl) && !a.synthetic);

        AxisControl Axis(string path) => string.IsNullOrEmpty(path) ? null : m_Device.TryGetChildControl<AxisControl>(path);

        void ApplyMapping()
        {
            var key = k_Prefs + DeviceKey + ".";
            if (PlayerPrefs.HasKey(key + "steer"))
            {
                m_Steering = Axis(PlayerPrefs.GetString(key + "steer"));
                m_SteeringCenter = PlayerPrefs.GetFloat(key + "steerCenter");
                m_SteeringSign = PlayerPrefs.GetFloat(key + "steerSign", 1f);
                m_Throttle.axis = Axis(PlayerPrefs.GetString(key + "throttle"));
                m_Throttle.rest = PlayerPrefs.GetFloat(key + "throttleRest", float.NaN);
                m_Throttle.travel = PlayerPrefs.GetFloat(key + "throttleTravel");
                m_Brake.axis = Axis(PlayerPrefs.GetString(key + "brake"));
                m_Brake.rest = PlayerPrefs.GetFloat(key + "brakeRest", float.NaN);
                m_Brake.travel = PlayerPrefs.GetFloat(key + "brakeTravel");
                m_ShifterFirst = PlayerPrefs.GetInt(key + "shiftFirst", 13);
                m_ShifterReverse = PlayerPrefs.GetInt(key + "shiftReverse", 19);
            }

            // G29 defaults (Windows, G HUB, separate pedals): wheel = X, accelerator = Y, brake = Rz.
            m_Steering ??= Axis("stick/x") ?? Axis("x") ?? Axes(m_Device).FirstOrDefault();
            m_Throttle.axis ??= Axis("stick/y") ?? Axis("y");
            m_Brake.axis ??= Axis("rz") ?? Axis("z");
            if (!PlayerPrefs.HasKey(key + "steer"))
                Debug.Log("[RoadReady] Wheel not calibrated yet: using G29 defaults. Press F3 to calibrate if the pedals or steering feel wrong.");
        }

        string DeviceKey => m_Device == null ? "" : $"{m_Device.description.product}".Replace(" ", "");

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.f2Key.wasPressedThisFrame) m_ShowMonitor = !m_ShowMonitor;
                if (keyboard.f3Key.wasPressedThisFrame && m_Device != null) m_Calibration = new Calibration(this);
            }

            if (m_Calibration != null)
            {
                Steer = Throttle = Brake = 0f;
                if (m_Calibration.Update())
                    m_Calibration = null;
                return;
            }

            if (Current == null)
            {
                Steer = Throttle = Brake = 0f;
                return;
            }

            var steer = Mathf.Clamp((m_Steering.ReadValue() - m_SteeringCenter) * m_SteeringSign, -1f, 1f);
            Steer = Mathf.Abs(steer) < k_Deadzone ? 0f : steer;
            Throttle = m_Throttle.Read();
            Brake = m_Brake.Read();
        }

        // ------------------------------------------------------------------ calibration

        /// <summary>Prompts: hands off, wheel right, accelerator, brake, shifter 1st, shifter R. Each is found by what moves.</summary>
        class Calibration
        {
            readonly RacingWheel m_Wheel;
            readonly List<AxisControl> m_Axes;
            readonly Dictionary<AxisControl, float> m_Rest = new Dictionary<AxisControl, float>();
            readonly List<ButtonControl> m_Buttons;
            readonly HashSet<ButtonControl> m_HeldAtRest = new HashSet<ButtonControl>();
            ButtonControl m_HeldButton;
            int m_Step;
            float m_Timer;
            AxisControl m_Candidate;

            public string Prompt => m_Step switch
            {
                0 => "Let go of the wheel and both pedals...",
                1 => "Turn the wheel all the way to the RIGHT and hold it.",
                2 => "Let the wheel go, then press the ACCELERATOR (right pedal) all the way down and hold it.",
                3 => "Release the accelerator, then press the BRAKE (middle pedal) all the way down and hold it.",
                4 => "Release the brake. Put the SHIFTER into 1st gear and hold it there.   (No shifter? Press SPACE to skip.)",
                5 => "Now put the SHIFTER into REVERSE (push the stick down, then right and back) and hold it.   (SPACE to skip.)",
                _ => "Done.",
            };

            public float Progress => m_Timer;

            public Calibration(RacingWheel wheel)
            {
                m_Wheel = wheel;
                m_Axes = Axes(wheel.m_Device).ToList();
                m_Buttons = wheel.m_Device.allControls.OfType<ButtonControl>().Where(b => !b.synthetic && b.name.StartsWith("button")).ToList();
            }

            /// <summary>Returns true when finished.</summary>
            public bool Update()
            {
                if (m_Step == 0)
                {
                    m_Timer += Time.unscaledDeltaTime / 2f;
                    if (m_Timer >= 1f)
                    {
                        foreach (var axis in m_Axes) m_Rest[axis] = axis.ReadValue();
                        foreach (var button in m_Buttons) if (button.isPressed) m_HeldAtRest.Add(button);
                        Next();
                    }

                    return false;
                }

                if (m_Step >= 4)
                    return UpdateShifter();

                // The axis that moved furthest from rest (excluding ones already assigned).
                AxisControl best = null;
                var bestDelta = 0f;
                foreach (var axis in m_Axes)
                {
                    if (axis == m_Wheel.m_Steering && m_Step > 1 || axis == m_Wheel.m_Throttle.axis && m_Step > 2)
                        continue;
                    var delta = Mathf.Abs(axis.ReadValue() - m_Rest[axis]);
                    if (delta > bestDelta) { bestDelta = delta; best = axis; }
                }

                if (best == null || bestDelta < 0.5f)
                {
                    m_Timer = 0f;
                    return false;
                }

                m_Candidate = best;
                m_Timer += Time.unscaledDeltaTime / 0.8f; // hold for 0.8 s
                if (m_Timer < 1f)
                    return false;

                var value = m_Candidate.ReadValue();
                var rest = m_Rest[m_Candidate];
                switch (m_Step)
                {
                    case 1:
                        m_Wheel.m_Steering = m_Candidate;
                        m_Wheel.m_SteeringCenter = rest;
                        m_Wheel.m_SteeringSign = value > rest ? 1f : -1f;
                        break;
                    case 2:
                        m_Wheel.m_Throttle.axis = m_Candidate;
                        m_Wheel.m_Throttle.rest = rest;
                        m_Wheel.m_Throttle.travel = value - rest;
                        break;
                    case 3:
                        m_Wheel.m_Brake.axis = m_Candidate;
                        m_Wheel.m_Brake.rest = rest;
                        m_Wheel.m_Brake.travel = value - rest;
                        break;
                }

                Next();
                return false;
            }

            /// <summary>Steps 4-5: the shifter button that goes down when the stick is put in 1st, then in R.</summary>
            bool UpdateShifter()
            {
                var keyboard = Keyboard.current;
                if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
                {
                    Save();
                    return true; // no shifter: keep the standard numbering
                }

                var held = m_Buttons.FirstOrDefault(b => b.isPressed && !m_HeldAtRest.Contains(b) && (m_Step == 4 || b != m_HeldButton));
                if (held == null)
                {
                    m_Timer = 0f;
                    return false;
                }

                m_Timer += Time.unscaledDeltaTime / 0.5f;
                if (m_Timer < 1f)
                    return false;
                var number = int.Parse(held.name.Substring("button".Length));
                if (m_Step == 4)
                {
                    m_Wheel.m_ShifterFirst = number;
                    m_HeldButton = held;
                    Next();
                    return false;
                }

                m_Wheel.m_ShifterReverse = number;
                Save();
                return true;
            }

            void Next()
            {
                m_Step++;
                m_Timer = 0f;
            }

            void Save()
            {
                var key = k_Prefs + m_Wheel.DeviceKey + ".";
                var w = m_Wheel;
                PlayerPrefs.SetString(key + "steer", RelativePath(w.m_Steering));
                PlayerPrefs.SetFloat(key + "steerCenter", w.m_SteeringCenter);
                PlayerPrefs.SetFloat(key + "steerSign", w.m_SteeringSign);
                PlayerPrefs.SetString(key + "throttle", RelativePath(w.m_Throttle.axis));
                PlayerPrefs.SetFloat(key + "throttleRest", w.m_Throttle.rest);
                PlayerPrefs.SetFloat(key + "throttleTravel", w.m_Throttle.travel);
                PlayerPrefs.SetString(key + "brake", RelativePath(w.m_Brake.axis));
                PlayerPrefs.SetFloat(key + "brakeRest", w.m_Brake.rest);
                PlayerPrefs.SetFloat(key + "brakeTravel", w.m_Brake.travel);
                PlayerPrefs.SetInt(key + "shiftFirst", w.m_ShifterFirst);
                PlayerPrefs.SetInt(key + "shiftReverse", w.m_ShifterReverse);
                PlayerPrefs.Save();
                Debug.Log($"[RoadReady] Wheel calibrated: steering={RelativePath(w.m_Steering)}, accelerator={RelativePath(w.m_Throttle.axis)}, brake={RelativePath(w.m_Brake.axis)}, shifter 1st=button{w.m_ShifterFirst}, R=button{w.m_ShifterReverse}.");
            }

            static string RelativePath(InputControl control) =>
                control == null ? "" : control.path.Substring(control.device.path.Length + 1);
        }

        // ------------------------------------------------------------------ on-screen monitor (flat screen only)

        void OnGUI()
        {
            if (m_Calibration == null && !m_ShowMonitor)
                return;
            var box = new Rect(20, 20, 560, m_Calibration != null ? 120 : 470);
            GUI.Box(box, GUIContent.none);
            GUILayout.BeginArea(new Rect(box.x + 12, box.y + 10, box.width - 24, box.height - 20));
            if (m_Calibration != null)
            {
                GUILayout.Label($"<b>Wheel calibration</b> - {DeviceName}", Rich());
                GUILayout.Label(m_Calibration.Prompt, Rich(18));
                GUILayout.HorizontalSlider(Mathf.Clamp01(m_Calibration.Progress), 0f, 1f);
                if (GUILayout.Button("Cancel")) m_Calibration = null;
            }
            else
            {
                GUILayout.Label(m_Device == null ? "<b>No wheel found.</b> Plug in the G29 (USB) and check it shows in Windows' 'Set up USB game controllers'." : $"<b>{DeviceName}</b>  (F3: calibrate, F2: hide)", Rich());
                var driver = RoadReadyApp.Instance != null && RoadReadyApp.Instance.Scenarios != null ? RoadReadyApp.Instance.Scenarios.Driver : null;
                GUILayout.Label(driver == null
                    ? "Car: not driving (start a Driver scenario)"
                    : $"Car: gear <b>{driver.Gear}</b>   handbrake <b>{(driver.Handbrake ? "UP (press Square)" : "down")}</b>   controls {(driver.InputEnabled ? "on" : "<b>off</b> (wait for the countdown / close the menu)")}   speed {driver.SpeedKmh:0} km/h", Rich());
                if (m_Device == null)
                    GUILayout.Label("Input devices Unity sees: " + string.Join(", ", InputSystem.devices.Select(d => $"{d.displayName} [{d.GetType().Name}]")), Rich(12));
                if (m_Device != null)
                {
                    GUILayout.Label($"Steer {Steer:+0.00;-0.00}   Accelerator {Throttle:0.00}   Brake {Brake:0.00}", Rich(16));
                    foreach (var axis in Axes(m_Device).Take(12))
                        GUILayout.Label($"{axis.path.Substring(m_Device.path.Length + 1),-12} {axis.ReadValue():+0.000;-0.000}" + (axis == m_Steering ? "  (steering)" : axis == m_Throttle.axis ? "  (accelerator)" : axis == m_Brake.axis ? "  (brake)" : ""));
                    var pressed = m_Device.allControls.OfType<ButtonControl>().Where(b => b.isPressed && !(b.parent is ButtonControl)).Select(b => b.path.Substring(m_Device.path.Length + 1));
                    GUILayout.Label("Pressed: " + string.Join(", ", pressed));
                }
            }

            GUILayout.EndArea();
        }

        static GUIStyle Rich(int size = 14) => new GUIStyle(GUI.skin.label) { richText = true, fontSize = size, wordWrap = true };
    }
}
