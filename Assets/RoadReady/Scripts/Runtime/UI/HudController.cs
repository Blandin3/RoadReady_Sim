using RoadReady.Config;
using RoadReady.Core;
using RoadReady.Scenarios;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoadReady.UI
{
    /// <summary>
    /// In-scenario HUD: speed / limit / indicators on a dashboard-anchored panel for the driver; prompts and
    /// captions on a lazily-following panel for the pedestrian. Deliberately shows no hazard hints, so it
    /// cannot contaminate the hazard perception measures.
    /// </summary>
    [RequireComponent(typeof(WorldSpacePanel))]
    public class HudController : MonoBehaviour
    {
        [SerializeField] float m_DashboardDistance = 0.95f;
        [SerializeField] float m_DashboardDrop = 0.34f;
        [SerializeField] float m_DashboardScale = 0.55f;
        [SerializeField] float m_FollowDistance = 1.4f;
        [SerializeField] float m_FollowDrop = 0.45f;
        [SerializeField] float m_FollowSmoothing = 3f;

        WorldSpacePanel m_Panel;
        ScenarioManager m_Scenarios;
        RoadReadyApp m_App;
        Label m_Speed, m_Limit, m_Timer, m_Gear, m_Countdown, m_Caption, m_Prompt, m_Toast, m_Tutorial, m_ArrowLeft, m_ArrowRight;
        VisualElement m_Cluster;
        Button m_Skip;
        bool m_SkipShown;
        float m_HandbrakeHintAt = -100f;
        float m_CaptionUntil, m_ToastUntil, m_CountdownUntil;
        Vector3 m_FollowPosition;
        bool m_HasFollowPosition;

        public void Initialize(RoadReadyApp app, ScenarioManager scenarios)
        {
            m_App = app;
            m_Scenarios = scenarios;
            m_Panel = GetComponent<WorldSpacePanel>();
            var root = m_Panel.Root;
            root.pickingMode = PickingMode.Ignore;
            m_Speed = root.Q<Label>("speed");
            m_Limit = root.Q<Label>("limit");
            m_Timer = root.Q<Label>("timer");
            m_Gear = root.Q<Label>("gear");
            m_Countdown = root.Q<Label>("countdown");
            m_Caption = root.Q<Label>("caption");
            m_Prompt = root.Q<Label>("prompt");
            m_Toast = root.Q<Label>("toast");
            m_Tutorial = root.Q<Label>("tutorial");
            m_ArrowLeft = root.Q<Label>("arrow-left");
            m_ArrowRight = root.Q<Label>("arrow-right");
            m_Cluster = root.Q<VisualElement>("cluster");
            m_Skip = root.Q<Button>("skip-tutorial");
            if (m_Skip != null)
                m_Skip.clicked += () => m_App.Pause(); // the same pause panel as the left controller's menu button

            RoadReadyEvents.InstructionRequested += OnInstruction;
            RoadReadyEvents.ToastRequested += OnToast;
            scenarios.CountdownTick += OnCountdown;
            scenarios.CollisionOccurred += what => OnToast($"Collision with {what}", 3f);
        }

        void OnDestroy()
        {
            RoadReadyEvents.InstructionRequested -= OnInstruction;
            RoadReadyEvents.ToastRequested -= OnToast;
        }

        void OnInstruction(string key)
        {
            var text = InstructionLibrary.Get(key);
            m_Caption.text = text;
            m_Caption.SetVisible(true);
            m_CaptionUntil = Time.unscaledTime + Mathf.Clamp(text.Length * 0.06f, 3f, 12f);

            if (key.StartsWith("tut_") && m_Scenarios.Request.phase == AttemptPhase.Tutorial && key != InstructionLibrary.TutDone)
            {
                m_Tutorial.text = text;
                m_Tutorial.SetVisible(true);
                m_Caption.SetVisible(false);
            }
        }

        void OnToast(string message, float seconds)
        {
            m_Toast.text = message;
            m_Toast.SetVisible(true);
            m_ToastUntil = Time.unscaledTime + seconds;
        }

        void OnCountdown(int value)
        {
            m_Countdown.text = value > 0 ? value.ToString() : "GO";
            m_Countdown.SetVisible(true);
            m_CountdownUntil = Time.unscaledTime + (value > 0 ? 1.2f : 0.8f);
        }

        void LateUpdate()
        {
            if (m_Panel == null || m_App == null)
                return;
            var now = Time.unscaledTime;
            if (now > m_CaptionUntil) m_Caption.SetVisible(false);
            if (now > m_ToastUntil) m_Toast.SetVisible(false);
            if (now > m_CountdownUntil) m_Countdown.SetVisible(false);

            var state = m_Scenarios.State;
            var inScenario = state == AttemptState.Countdown || state == AttemptState.Running;
            if (!inScenario || m_Scenarios.Request.phase != AttemptPhase.Tutorial)
                m_Tutorial.SetVisible(false);
            UpdateSkipButton(state);

            var driver = inScenario ? m_Scenarios.Driver : null;
            m_Cluster.SetVisible(driver != null);
            if (driver != null)
            {
                var speed = driver.SpeedKmh;
                var limit = m_Scenarios.CurrentSpeedLimit;
                m_Speed.text = Mathf.RoundToInt(speed).ToString();
                m_Limit.text = Mathf.RoundToInt(limit).ToString();
                m_Limit.EnableInClassList("hud__limit--over", speed > limit + 3f);
                var blink = Mathf.Repeat(Time.time, 0.7f) < 0.35f;
                m_ArrowLeft.EnableInClassList("hud__arrow--on", driver.Indicator == TurnSide.Left && blink);
                m_ArrowRight.EnableInClassList("hud__arrow--on", driver.Indicator == TurnSide.Right && blink);
                if (m_Gear != null)
                    m_Gear.text = GearText(driver.Gear, driver.Handbrake);
                // Trying to drive away with the handbrake on: remind the learner (at most every few seconds).
                if (driver.Handbrake && driver.Throttle > 0.3f && driver.Gear != Gear.Park && driver.Gear != Gear.Neutral && now - m_HandbrakeHintAt > 5f)
                {
                    m_HandbrakeHintAt = now;
                    OnToast("Handbrake is on: push the lever down (or press H) to drive", 3f);
                }
                var remaining = m_Scenarios.TimeRemaining;
                m_Timer.text = state == AttemptState.Running && m_Scenarios.Request.phase != AttemptPhase.Tutorial ? $"{(int)remaining / 60}:{(int)remaining % 60:00}" : "";
            }

            var prompt = inScenario && m_Scenarios.Pedestrian != null && m_Scenarios.Pedestrian.enabled ? m_Scenarios.Pedestrian.Prompt : null;
            m_Prompt.text = prompt ?? "";
            m_Prompt.SetVisible(!string.IsNullOrEmpty(prompt) && !m_App.UI.MenuVisible);

            Place(driver);
        }

        /// <summary>"P R N D" with the engaged gear bright and the others dimmed.</summary>
        /// <summary>
        /// During the tutorial a "Skip tutorial" button sits under the instruction. It opens the pause panel (where
        /// the learner confirms the skip), exactly like the left controller's menu button, which keeps working too.
        /// The controller ray stays on while the button is shown so it can be pointed at.
        /// </summary>
        void UpdateSkipButton(AttemptState state)
        {
            if (m_Skip == null)
                return;
            var show = state == AttemptState.Running && m_Scenarios.Request.phase == AttemptPhase.Tutorial
                       && m_App.State == AppState.InScenario && !m_App.UI.MenuVisible;
            m_Skip.SetVisible(show);
            if (show != m_SkipShown)
                m_Panel.SetInteractive(show);
            if (show && !m_App.Rig.PointersVisible)
                m_App.Rig.SetPointers(true);
            else if (!show && m_SkipShown && !m_App.UI.MenuVisible && m_App.State == AppState.InScenario)
                m_App.Rig.SetPointers(false);
            m_SkipShown = show;
        }

        static string GearText(Gear gear, bool handbrake)
        {
            var sb = new System.Text.StringBuilder();
            foreach (Gear g in System.Enum.GetValues(typeof(Gear)))
                sb.Append(g == gear ? $"<color=#FAD201>{g.ToString()[0]}</color> " : $"<color=#5A6573>{g.ToString()[0]}</color> ");
            if (handbrake)
                sb.Append("  <color=#E5484D>(P) BRAKE</color>");
            return sb.ToString().TrimEnd();
        }

        void Place(Player.DriverController driver)
        {
            var head = m_App.Rig.HeadTransform;
            if (driver != null && driver.EyePoint != null)
            {
                // Fixed to the car so it moves with the dashboard, not with the head (less sickness).
                var eye = driver.EyePoint;
                m_Panel.SetScaleMultiplier(m_DashboardScale);
                var position = eye.position + eye.forward * m_DashboardDistance - eye.up * m_DashboardDrop;
                m_Panel.PlaceAt(position, eye.position);
                m_HasFollowPosition = false;
                return;
            }

            m_Panel.SetScaleMultiplier(1f);
            var forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            var target = head.position + forward.normalized * m_FollowDistance - Vector3.up * m_FollowDrop;
            m_FollowPosition = m_HasFollowPosition ? Vector3.Lerp(m_FollowPosition, target, 1f - Mathf.Exp(-m_FollowSmoothing * Time.unscaledDeltaTime)) : target;
            m_HasFollowPosition = true;
            m_Panel.PlaceAt(m_FollowPosition, head.position);
        }
    }
}
