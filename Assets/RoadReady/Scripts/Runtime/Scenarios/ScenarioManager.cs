using System;
using System.Collections;
using System.Collections.Generic;
using RoadReady.Config;
using RoadReady.Core;
using RoadReady.Data;
using RoadReady.Hazards;
using RoadReady.Player;
using RoadReady.Rules;
using RoadReady.Scoring;
using RoadReady.Traffic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoadReady.Scenarios
{
    public struct AttemptRequest
    {
        public ScenarioDefinition scenario;
        public Perspective perspective;
        public int level;
        public AttemptPhase phase;
    }

    public interface IScreenFader
    {
        IEnumerator FadeTo(float alpha, float seconds);
    }

    public enum AttemptState
    {
        Idle,
        Loading,
        Countdown,
        Running,
        Finished
    }

    /// <summary>
    /// Runs one scenario attempt end to end: loads the scene, seats / stands the learner, runs hazards, rules and
    /// telemetry, then scores, saves (FR10) and hands a feedback report to the UI (FR8).
    /// </summary>
    public class ScenarioManager : MonoBehaviour
    {
        [SerializeField] PlayerRig m_Rig;
        [SerializeField] GazeTracker m_Gaze;
        [SerializeField] PedestrianController m_Pedestrian;
        [SerializeField] MotionComfortVignette m_Vignette;
        [SerializeField] int m_CountdownSeconds = 3;
        [Tooltip("Lobby geometry and lighting, hidden while a scenario scene is loaded.")]
        [SerializeField] GameObject m_Lobby;

        readonly HazardMonitor m_Hazards = new HazardMonitor();
        readonly RuleEngine m_Rules = new RuleEngine();
        readonly List<DecisionRecord> m_Decisions = new List<DecisionRecord>();

        RoadReadyConfig m_Config;
        DataService m_Data;
        ScenarioOverrideStore m_Overrides;
        IScreenFader m_Fader;
        Scene m_LoadedScene;
        Scene m_HomeScene;
        ScenarioContext m_Context;
        AttemptRequest m_Request;
        ScenarioParameters m_Parameters;
        AttemptRecord m_Attempt;
        TelemetryWriter m_Telemetry;
        DriverController m_Driver;
        IRoadAgent m_Player;
        AttemptEndReason? m_PendingEnd;
        float m_SampleTimer;
        int m_DecisionCounter;
        bool m_Paused;

        public event Action<AttemptState> StateChanged;
        public event Action<int> CountdownTick;
        public event Action<string> CollisionOccurred;
        public event Action<AttemptRecord, FeedbackReport> AttemptFinished;
        public event Action<string> AttemptFailed;

        public AttemptState State { get; private set; }
        public AttemptRequest Request => m_Request;
        public ScenarioParameters Parameters => m_Parameters;
        public ScenarioContext Context => m_Context;
        public DriverController Driver => m_Driver;
        public PedestrianController Pedestrian => m_Pedestrian;
        public PlayerRig Rig => m_Rig;
        public GazeTracker Gaze => m_Gaze;
        public bool IsPaused => m_Paused;
        public float TimeRemaining => m_Parameters != null && State == AttemptState.Running ? Mathf.Max(0f, m_Parameters.timeLimitSeconds - SimClock.Now) : 0f;
        public bool HasScenarioLoaded => m_LoadedScene.IsValid() && m_LoadedScene.isLoaded;

        public float CurrentSpeedLimit =>
            m_Driver != null && m_Parameters != null ? SpeedLimitZone.GetLimitAt(m_Driver.Position, m_Parameters.speedLimitKmh) : 0f;

        public void Initialize(RoadReadyConfig config, DataService data, ScenarioOverrideStore overrides, IScreenFader fader)
        {
            m_Config = config;
            m_Data = data;
            m_Overrides = overrides;
            m_Fader = fader;
            m_HomeScene = gameObject.scene;
            m_Hazards.HazardResolved += OnHazardResolved;
            if (m_Pedestrian != null)
                m_Pedestrian.enabled = false;
        }

        public void StartAttempt(AttemptRequest request)
        {
            if (State == AttemptState.Loading || State == AttemptState.Countdown || State == AttemptState.Running)
                return;
            StartCoroutine(RunAttempt(request));
        }

        public void AbortAttempt()
        {
            if (State == AttemptState.Running)
                m_PendingEnd = AttemptEndReason.Aborted;
        }

        public void SkipTutorial()
        {
            if (State == AttemptState.Running && m_Request.phase == AttemptPhase.Tutorial)
                m_PendingEnd = AttemptEndReason.TutorialComplete;
        }

        public void SetPaused(bool paused)
        {
            m_Paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            SetInputEnabled(!paused && State == AttemptState.Running);
        }

        public void Recenter() => m_Rig.Recenter();

        void SetState(AttemptState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }

        // ------------------------------------------------------------------ load

        IEnumerator RunAttempt(AttemptRequest request)
        {
            m_Request = request;
            SetState(AttemptState.Loading);
            if (m_Fader != null)
                yield return m_Fader.FadeTo(1f, 0.35f);

            yield return UnloadRoutine();

            var sceneName = request.scenario.sceneName;
            if (string.IsNullOrEmpty(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Fail($"Scene '{sceneName}' for scenario '{request.scenario.displayName}' is not in Build Settings.");
                yield break;
            }

            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            while (!op.isDone)
                yield return null;
            m_LoadedScene = SceneManager.GetSceneByName(sceneName);
            SceneManager.SetActiveScene(m_LoadedScene);
            if (m_Lobby != null)
                m_Lobby.SetActive(false);

            m_Context = null;
            foreach (var root in m_LoadedScene.GetRootGameObjects())
            {
                m_Context = root.GetComponentInChildren<ScenarioContext>(true);
                if (m_Context != null) break;
            }

            if (m_Context == null)
            {
                Fail($"Scene '{sceneName}' has no ScenarioContext.");
                yield break;
            }

            m_Parameters = m_Overrides.Resolve(request.scenario, request.level);
            RoadReadyRandom.Init(m_Parameters.randomSeed);
            m_Context.Prepare(m_Parameters, request.perspective);
            PlacePlayer(request.perspective);
            m_Gaze.ClearHistory();

            if (m_Fader != null)
                yield return m_Fader.FadeTo(0f, 0.5f);

            if (request.phase != AttemptPhase.Tutorial)
            {
                SetState(AttemptState.Countdown);
                RoadReadyEvents.RequestInstruction(InstructionLibrary.AttemptStart);
                for (var i = m_CountdownSeconds; i > 0; i--)
                {
                    CountdownTick?.Invoke(i);
                    yield return new WaitForSeconds(1f);
                }

                CountdownTick?.Invoke(0);
            }

            BeginAttempt();
        }

        void Fail(string message)
        {
            Debug.LogError("[RoadReady] " + message);
            SetState(AttemptState.Idle);
            if (m_Fader != null)
                StartCoroutine(m_Fader.FadeTo(0f, 0.3f));
            AttemptFailed?.Invoke(message);
        }

        void PlacePlayer(Perspective perspective)
        {
            if (perspective == Perspective.Driver)
            {
                m_Driver = m_Context.PlayerVehicle;
                if (m_Driver == null)
                {
                    Debug.LogError("[RoadReady] Scenario has no player vehicle for the driver perspective.");
                    return;
                }

                m_Driver.ResetTo(m_Context.GetSpawn(Perspective.Driver));
                m_Driver.InputEnabled = false;
                m_Pedestrian.enabled = false;
                m_Rig.SetLocomotion(false);
                m_Rig.AttachToSeat(m_Driver.EyePoint, m_Driver.transform);
                m_Player = m_Driver;
            }
            else
            {
                m_Driver = null;
                m_Rig.PlaceStanding(m_Context.GetSpawn(Perspective.Pedestrian));
                m_Pedestrian.enabled = true;
                m_Pedestrian.ResetState();
                m_Pedestrian.InputEnabled = false;
                m_Rig.SetLocomotion(false);
                m_Player = m_Pedestrian;
            }

            m_Rig.SetPointers(false);
            if (m_Vignette != null)
            {
                m_Vignette.Driver = m_Driver;
                m_Vignette.Pedestrian = perspective == Perspective.Pedestrian ? m_Pedestrian : null;
            }
        }

        // ------------------------------------------------------------------ run

        void BeginAttempt()
        {
            var participant = m_Data.CurrentParticipant;
            var previous = participant != null ? m_Data.GetScoredAttempts(participant.participantId, m_Request.scenario.scenarioId, m_Request.perspective) : new List<AttemptRecord>();

            m_Decisions.Clear();
            m_DecisionCounter = 0;
            m_PendingEnd = null;
            m_SampleTimer = 0f;
            SimClock.Begin();

            m_Attempt = new AttemptRecord
            {
                attemptId = participant != null ? m_Data.NewAttemptId(participant) : "RR-DEMO-" + DateTime.UtcNow.Ticks,
                sessionId = m_Data.CurrentSession?.sessionId,
                participantId = participant?.participantId ?? "RR-DEMO",
                scenarioId = m_Request.scenario.scenarioId,
                scenarioName = m_Request.scenario.displayName,
                scenarioType = m_Request.scenario.type,
                perspective = m_Request.perspective,
                difficultyLevel = m_Request.level,
                phase = m_Request.phase,
                condition = participant?.condition ?? StudyCondition.RoadReadyTraining,
                attemptNumber = m_Request.phase == AttemptPhase.Tutorial ? 0 : previous.Count + 1,
                startUtc = DateTime.UtcNow.ToString("o"),
                randomSeed = m_Parameters.randomSeed,
                parameters = m_Parameters.Clone(),
                appVersion = Application.version,
            };

            m_Telemetry = participant != null ? new TelemetryWriter(m_Data.TelemetryPath(participant.participantId, m_Attempt.attemptId)) : null;
            m_Telemetry?.WriteEvent("attempt_start", 0f, m_Attempt.scenarioId, $"{m_Attempt.perspective}|L{m_Attempt.difficultyLevel}|{m_Attempt.phase}|seed={m_Attempt.randomSeed}");

            m_Hazards.Begin(m_Request.perspective, m_Config.scoring, m_Context.Triggers, m_Player, m_Driver, m_Request.perspective == Perspective.Pedestrian ? m_Pedestrian : null, m_Gaze, m_Telemetry);
            m_Rules.Begin(new RuleContext
            {
                perspective = m_Request.perspective,
                scoring = m_Config.scoring,
                parameters = m_Parameters,
                player = m_Player,
                driver = m_Driver,
                pedestrian = m_Request.perspective == Perspective.Pedestrian ? m_Pedestrian : null,
                gaze = m_Gaze,
                isHazardActor = m_Hazards.IsHazardActor,
                onCollision = OnCollision,
                onDecision = AddDecision,
            });

            if (m_Request.phase == AttemptPhase.Tutorial && m_Context.Tutorial != null)
            {
                m_Context.Tutorial.Completed += OnTutorialCompleted;
                m_Context.Tutorial.Begin(m_Request.perspective, m_Driver, m_Pedestrian, m_Gaze);
            }

            SetInputEnabled(true);
            if (m_Request.perspective == Perspective.Pedestrian)
                m_Rig.SetLocomotion(true);
            SetState(AttemptState.Running);
        }

        void OnTutorialCompleted()
        {
            m_Context.Tutorial.Completed -= OnTutorialCompleted;
            m_PendingEnd = AttemptEndReason.TutorialComplete;
        }

        void SetInputEnabled(bool enabled)
        {
            var input = RoadReadyInput.Instance;
            var driving = enabled && m_Request.perspective == Perspective.Driver;
            var walking = enabled && m_Request.perspective == Perspective.Pedestrian;
            input.SetDriving(driving);
            input.SetWalking(walking);
            if (m_Driver != null) m_Driver.InputEnabled = driving;
            if (m_Pedestrian != null) m_Pedestrian.InputEnabled = walking;
        }

        void Update()
        {
            if (State != AttemptState.Running || m_Paused)
                return;

            if (RoadReadyInput.Instance.HazardSpotted.WasPressedThisFrame())
            {
                RoadReadyEvents.RaiseHazardSpotted();
                AddDecisionTelemetry("spotted");
            }

            if (RoadReadyInput.Instance.Recenter.WasPressedThisFrame())
                m_Rig.Recenter();

            var dt = Time.deltaTime;
            m_Hazards.Tick(dt);
            m_Rules.Tick(dt);
            SampleTelemetry(dt);

            if (m_PendingEnd == null)
            {
                if (m_Request.phase != AttemptPhase.Tutorial && m_Context.IsGoalReached(m_Request.perspective, m_Player, m_Pedestrian))
                    m_PendingEnd = AttemptEndReason.GoalReached;
                else if (m_Request.phase != AttemptPhase.Tutorial && SimClock.Now >= m_Parameters.timeLimitSeconds)
                    m_PendingEnd = AttemptEndReason.TimeLimit;
            }

            if (m_PendingEnd != null)
                FinishAttempt(m_PendingEnd.Value);
        }

        void OnCollision(IRoadAgent other, string what)
        {
            m_Hazards.NotifyCollision(other);
            CollisionOccurred?.Invoke(what);
            m_Telemetry?.WriteEvent("collision", SimClock.Now, what);
            if (m_Parameters.endAttemptOnCollision && m_Request.phase != AttemptPhase.Tutorial)
            {
                m_Driver?.Stop();
                m_PendingEnd = AttemptEndReason.Collision; // deferred: we may be inside a registry iteration
            }
        }

        void OnHazardResolved(PerformanceRecord record, DecisionRecord decision) => AddDecision(decision);

        void AddDecision(DecisionRecord decision)
        {
            decision.decisionId = $"D{++m_DecisionCounter:000}";
            m_Decisions.Add(decision);
            m_Telemetry?.WriteEvent("decision", decision.time, decision.ruleId, $"{decision.decision}|{decision.severity}|{decision.title}");
        }

        void AddDecisionTelemetry(string kind) => m_Telemetry?.WriteEvent(kind, SimClock.Now, null);

        void SampleTelemetry(float dt)
        {
            if (m_Telemetry == null)
                return;
            m_SampleTimer += dt;
            var interval = 1f / Mathf.Max(1f, m_Config.telemetrySampleRate);
            if (m_SampleTimer < interval)
                return;
            m_SampleTimer = 0f;

            var zone = RoadZone.FindFirst(m_Player.Position);
            var reference = m_Driver != null ? m_Driver.Forward : Vector3.forward;
            m_Telemetry.Write(new TelemetrySample
            {
                kind = "s",
                t = SimClock.Now,
                x = m_Player.Position.x,
                z = m_Player.Position.z,
                yaw = m_Player.Transform.eulerAngles.y,
                headYaw = m_Driver != null ? m_Gaze.YawRelativeTo(reference) : m_Gaze.HeadWorldYaw,
                headPitch = m_Gaze.HeadPitch,
                speedKmh = m_Player.Speed * Units.MsToKmh,
                throttle = m_Driver != null ? m_Driver.Throttle : 0f,
                brake = m_Driver != null ? m_Driver.Brake : 0f,
                steer = m_Driver != null ? m_Driver.Steer : 0f,
                zone = zone != null ? zone.Type.ToString() : "none",
            });
        }

        // ------------------------------------------------------------------ finish

        void FinishAttempt(AttemptEndReason reason)
        {
            m_PendingEnd = null;
            SetInputEnabled(false);
            m_Rig.SetLocomotion(false);
            if (m_Context.Tutorial != null && m_Context.Tutorial.IsRunning)
            {
                m_Context.Tutorial.Completed -= OnTutorialCompleted;
                m_Context.Tutorial.Stop();
            }

            m_Hazards.End();
            m_Rules.End();
            var duration = SimClock.Now;
            SimClock.Stop();

            m_Attempt.endUtc = DateTime.UtcNow.ToString("o");
            m_Attempt.durationSeconds = duration;
            m_Attempt.endReason = reason;
            m_Attempt.completed = reason == AttemptEndReason.GoalReached || reason == AttemptEndReason.TutorialComplete;
            m_Attempt.hazardRecords = new List<PerformanceRecord>(m_Hazards.Records);
            m_Attempt.decisions = new List<DecisionRecord>(m_Decisions);
            m_Attempt.score = ScoreCalculator.Compute(m_Attempt.hazardRecords, m_Attempt.decisions, m_Config.scoring);
            m_Telemetry?.WriteEvent("attempt_end", duration, reason.ToString(), $"score={m_Attempt.score.total}");
            m_Telemetry?.Dispose();
            m_Telemetry = null;

            var participant = m_Data.CurrentParticipant;
            var unlocked = false;
            List<AttemptRecord> previous = null;
            if (participant != null)
            {
                previous = m_Data.GetScoredAttempts(participant.participantId, m_Attempt.scenarioId, m_Attempt.perspective);
                m_Data.SaveAttempt(m_Attempt);

                if (m_Attempt.phase == AttemptPhase.Tutorial)
                {
                    participant.SetTutorialDone(m_Attempt.perspective);
                    m_Data.SaveParticipant(participant);
                }
                else if (reason != AttemptEndReason.Aborted)
                {
                    var current = participant.GetUnlockedLevel(m_Attempt.scenarioId, m_Attempt.perspective);
                    if (m_Attempt.score.total >= m_Request.scenario.unlockNextLevelScore && m_Attempt.difficultyLevel >= current && current < m_Request.scenario.LevelCount)
                    {
                        participant.SetUnlockedLevel(m_Attempt.scenarioId, m_Attempt.perspective, current + 1);
                        m_Data.SaveParticipant(participant);
                        unlocked = true;
                    }
                }
            }

            RoadReadyEvents.RequestInstruction(reason switch
            {
                AttemptEndReason.Collision => InstructionLibrary.Collision,
                AttemptEndReason.TimeLimit => InstructionLibrary.TimeUp,
                AttemptEndReason.TutorialComplete => InstructionLibrary.TutDone,
                _ => InstructionLibrary.AttemptComplete,
            });

            var report = FeedbackGenerator.Build(m_Attempt, m_Request.scenario, previous, unlocked);
            SetState(AttemptState.Finished);
            AttemptFinished?.Invoke(m_Attempt, report);
        }

        // ------------------------------------------------------------------ unload

        public void UnloadScenario(Action onDone = null) => StartCoroutine(UnloadWithFade(onDone));

        IEnumerator UnloadWithFade(Action onDone)
        {
            if (m_Fader != null)
                yield return m_Fader.FadeTo(1f, 0.3f);
            yield return UnloadRoutine();
            if (m_Fader != null)
                yield return m_Fader.FadeTo(0f, 0.4f);
            onDone?.Invoke();
        }

        IEnumerator UnloadRoutine()
        {
            if (State == AttemptState.Running)
                FinishAttempt(AttemptEndReason.Aborted);

            // The rig must leave the scenario scene before it unloads, or it is destroyed with it.
            m_Pedestrian.enabled = false;
            m_Rig.ReturnToLobby();
            m_Rig.SetPointers(true);
            if (m_Vignette != null)
            {
                m_Vignette.Driver = null;
                m_Vignette.Pedestrian = null;
            }

            if (m_Context != null)
                m_Context.Shutdown();
            m_Context = null;
            m_Driver = null;
            m_Player = null;

            if (m_LoadedScene.IsValid() && m_LoadedScene.isLoaded)
            {
                if (m_HomeScene.IsValid())
                    SceneManager.SetActiveScene(m_HomeScene);
                var op = SceneManager.UnloadSceneAsync(m_LoadedScene);
                while (op != null && !op.isDone)
                    yield return null;
            }

            m_LoadedScene = default;
            if (m_Lobby != null)
                m_Lobby.SetActive(true);
            SetState(AttemptState.Idle);
        }

        void OnApplicationPause(bool paused)
        {
            // Headset removed / app backgrounded on Quest: persist what we have (NFR reliability).
            if (paused && State == AttemptState.Running)
                FinishAttempt(AttemptEndReason.Aborted);
        }
    }
}
