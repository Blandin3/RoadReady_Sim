using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using RoadReady.Config;
using RoadReady.Core;
using RoadReady.Traffic;
using UnityEngine;

namespace RoadReady.Guidance
{
    /// <summary>Which on-screen/voice instruction key belongs to a cue in a mode.</summary>
    public static class GuidanceCues
    {
        public static string Key(GuidanceLabel label, GuidanceMode mode)
        {
            var coach = mode == GuidanceMode.Coaching;
            switch (label)
            {
                case GuidanceLabel.SlowDown: return coach ? InstructionLibrary.GuideSlowDownCoach : InstructionLibrary.GuideSlowDown;
                case GuidanceLabel.CrossingAhead: return coach ? InstructionLibrary.GuideCrossingAheadCoach : InstructionLibrary.GuideCrossingAhead;
                case GuidanceLabel.StopAhead: return coach ? InstructionLibrary.GuideStopAheadCoach : InstructionLibrary.GuideStopAhead;
                case GuidanceLabel.LaneDeparture: return coach ? InstructionLibrary.GuideLaneDepartureCoach : InstructionLibrary.GuideLaneDeparture;
                default: return null;
            }
        }
    }

    /// <summary>
    /// The real-time driving guide for one attempt. Several times a second it measures the car's surroundings,
    /// asks the rule-based guide which label applies and, when the scheduler allows, shows a caption and plays the
    /// voice clip (through the existing instruction path, which already respects the voice setting and the volume).
    /// It never touches scoring or the rule engine.
    /// </summary>
    public sealed class GuidanceEngine
    {
        // The name written to the log next to every cue, so cues from the rule guide and (later) a model can be told apart.
        public const string PredictorName = "rule";

        readonly GuidanceSettings m_Settings;
        readonly GuidanceFeatureExtractor m_Extractor;
        readonly GuidanceScheduler m_Scheduler;
        readonly Action<string, string> m_Log;

        GuidanceMode m_Mode = GuidanceMode.Off;
        IRoadAgent m_Player;
        float m_DefaultLimit;
        float m_Timer;
        GuidanceCsvLogger m_Csv;
        string m_AttemptId;

        /// <summary>Evaluations done this attempt, and the time they took (feature measurement + rule label + scheduling, in ms).</summary>
        public int Evaluations { get; private set; }
        public double TotalMs { get; private set; }
        public double MaxMs { get; private set; }
        public int CuesShown { get; private set; }
        public double AverageMs => Evaluations > 0 ? TotalMs / Evaluations : 0.0;

        /// <param name="log">Called with (id, detail) for telemetry events, may be null.</param>
        public GuidanceEngine(GuidanceSettings settings, ScoringConfig scoring, Action<string, string> log = null)
        {
            m_Settings = settings ?? new GuidanceSettings();
            m_Extractor = new GuidanceFeatureExtractor(scoring);
            m_Scheduler = new GuidanceScheduler(m_Settings);
            m_Log = log;
        }

        /// <summary>True when the engine measures the car (cues on, or developer logging on).</summary>
        public bool Running { get; private set; }

        public void Begin(GuidanceMode mode, IRoadAgent player, float defaultSpeedLimitKmh, string attemptId)
        {
            m_Mode = mode;
            m_Player = player;
            m_DefaultLimit = defaultSpeedLimitKmh;
            m_AttemptId = attemptId;
            m_Timer = 0f;
            Evaluations = 0;
            TotalMs = 0.0;
            MaxMs = 0.0;
            CuesShown = 0;
            m_Scheduler.Reset(0f);
            m_Csv?.Dispose();
            m_Csv = null;

            var logging = m_Settings.logFeatureRows && (Application.isEditor || UnityEngine.Debug.isDebugBuild) && player != null;
            if (logging)
                m_Csv = GuidanceCsvLogger.Open(Path.Combine(Application.persistentDataPath, "RoadReady", "dev", "guidance_rows.csv"));
            Running = player != null && (mode != GuidanceMode.Off || m_Csv != null);
        }

        /// <summary>Call every frame while the attempt runs. <paramref name="now"/> is the attempt clock in seconds.</summary>
        public void Tick(float dt, float now)
        {
            if (!Running)
                return;
            m_Timer += dt;
            var interval = 1f / Mathf.Clamp(m_Settings.evaluationsPerSecond, 5f, 10f);
            if (m_Timer < interval)
                return;
            m_Timer = m_Timer > 2f * interval ? 0f : m_Timer - interval;

            var started = Stopwatch.GetTimestamp();
            var features = m_Extractor.Extract(m_Player, m_DefaultLimit);
            var label = GuidanceRules.Classify(features, m_Settings);
            var cue = m_Mode != GuidanceMode.Off ? m_Scheduler.Update(label, now) : GuidanceLabel.Safe;
            var ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            Evaluations++;
            TotalMs += ms;
            if (ms > MaxMs)
                MaxMs = ms;

            m_Csv?.Write(m_AttemptId, now, features, label);

            if (cue != GuidanceLabel.Safe)
            {
                CuesShown++;
                RoadReadyEvents.RequestInstruction(GuidanceCues.Key(cue, m_Mode));
                m_Log?.Invoke("guidance", $"{cue}|{PredictorName}|{m_Mode}");
            }
        }

        /// <summary>Closes the attempt: flushes the developer log and records the timing so it can be read on the headset.</summary>
        public void End()
        {
            if (m_Player != null && Evaluations > 0)
            {
                var summary = string.Format(CultureInfo.InvariantCulture, "{0}|calls={1}|avgMs={2:0.000}|maxMs={3:0.000}|cues={4}", PredictorName, Evaluations, AverageMs, MaxMs, CuesShown);
                m_Log?.Invoke("guidance_timing", summary);
                UnityEngine.Debug.Log("[RoadReady] Guidance timing: " + summary);
            }

            m_Csv?.Dispose();
            m_Csv = null;
            Running = false;
            m_Player = null;
        }
    }

    /// <summary>Developer-only CSV of (features + rule label) rows: the "real" training data.</summary>
    public sealed class GuidanceCsvLogger : IDisposable
    {
        public const string Header = "source,attemptId," + "t," + GuidanceFeatures.CsvHeader + ",label";

        StreamWriter m_Writer;

        public static GuidanceCsvLogger Open(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var isNew = !File.Exists(path) || new FileInfo(path).Length == 0;
                var logger = new GuidanceCsvLogger { m_Writer = new StreamWriter(path, append: true) };
                if (isNew)
                    logger.m_Writer.WriteLine(Header);
                return logger;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[RoadReady] Guidance feature log disabled: " + e.Message);
                return null;
            }
        }

        public void Write(string attemptId, float t, in GuidanceFeatures features, GuidanceLabel label)
        {
            if (m_Writer == null)
                return;
            try
            {
                m_Writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "real,{0},{1:0.##},{2},{3}", attemptId, t, features.ToCsv(), label));
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[RoadReady] Guidance feature log write failed: " + e.Message);
                Dispose();
            }
        }

        public void Dispose()
        {
            m_Writer?.Dispose();
            m_Writer = null;
        }
    }
}
