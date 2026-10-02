using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoadReady.Core;
using UnityEngine;

namespace RoadReady.Data
{
    /// <summary>
    /// Local persistent store for all study data (FR10). Layout under persistentDataPath/RoadReady:
    /// <code>
    ///   participants/{pid}/participant.json
    ///   participants/{pid}/sessions/{sessionId}.json
    ///   participants/{pid}/attempts/{attemptId}.json
    ///   participants/{pid}/telemetry/{attemptId}.ndjson   (incremental, crash-safe)
    ///   exports/{timestamp}/*.csv
    /// </code>
    /// Every JSON file is written via temp-file + replace so a crash never leaves a half-written record
    /// (NFR reliability). Only anonymised identifiers are stored (NFR data privacy).
    /// </summary>
    public class DataService
    {
        const string k_IdAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        readonly Dictionary<string, List<AttemptRecord>> m_AttemptCache = new Dictionary<string, List<AttemptRecord>>();

        public string RootFolder { get; }
        public string ParticipantsFolder => Path.Combine(RootFolder, "participants");
        public string ExportsFolder => Path.Combine(RootFolder, "exports");

        public ParticipantRecord CurrentParticipant { get; private set; }
        public SessionRecord CurrentSession { get; private set; }

        /// <summary>Raised after any record is written, with (collection, id, json). Used by <see cref="RemoteSync"/>.</summary>
        public event Action<string, string, string> RecordSaved;
        public event Action<string> ParticipantDeleted;

        public DataService(string rootFolder = null)
        {
            RootFolder = rootFolder ?? Path.Combine(Application.persistentDataPath, "RoadReady");
            Directory.CreateDirectory(ParticipantsFolder);
        }

        // ----------------------------------------------------------------- participants

        public ParticipantRecord CreateParticipant(Perspective group, StudyCondition condition, AgeBand ageBand, DrivingExperience experience, bool guardianConsentVerified)
        {
            var record = new ParticipantRecord
            {
                participantId = GenerateParticipantId(),
                createdUtc = DateTime.UtcNow.ToString("o"),
                group = group,
                condition = condition,
                ageBand = ageBand,
                drivingExperience = experience,
                guardianConsentVerified = guardianConsentVerified,
            };
            SaveParticipant(record);
            CurrentParticipant = record;
            return record;
        }

        public void SetCurrentParticipant(ParticipantRecord participant) => CurrentParticipant = participant;

        public void RecordConsent(ParticipantRecord participant, bool consent)
        {
            participant.consentGiven = consent;
            participant.consentUtc = DateTime.UtcNow.ToString("o");
            SaveParticipant(participant);
        }

        public void SaveParticipant(ParticipantRecord participant)
        {
            var folder = ParticipantFolder(participant.participantId);
            WriteJsonAtomic(Path.Combine(folder, "participant.json"), participant, "participants", participant.participantId);
        }

        public ParticipantRecord LoadParticipant(string participantId)
        {
            var path = Path.Combine(ParticipantFolder(participantId), "participant.json");
            return ReadJson<ParticipantRecord>(path);
        }

        public List<string> ListParticipantIds()
        {
            if (!Directory.Exists(ParticipantsFolder))
                return new List<string>();
            return Directory.GetDirectories(ParticipantsFolder)
                .Select(Path.GetFileName)
                .Where(id => File.Exists(Path.Combine(ParticipantsFolder, id, "participant.json")))
                .OrderBy(id => id)
                .ToList();
        }

        public List<ParticipantRecord> LoadAllParticipants() =>
            ListParticipantIds().Select(LoadParticipant).Where(p => p != null).ToList();

        /// <summary>"Withdraw from study" use case: removes every local file for the participant.</summary>
        public void DeleteParticipant(string participantId)
        {
            var folder = ParticipantFolder(participantId, create: false);
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
            m_AttemptCache.Remove(participantId);
            if (CurrentParticipant != null && CurrentParticipant.participantId == participantId)
            {
                CurrentParticipant = null;
                CurrentSession = null;
            }

            ParticipantDeleted?.Invoke(participantId);
        }

        // ----------------------------------------------------------------- sessions

        public SessionRecord BeginSession(ParticipantRecord participant, bool isPostTest)
        {
            participant.sessionCount++;
            SaveParticipant(participant);
            CurrentSession = new SessionRecord
            {
                sessionId = $"{participant.participantId}-S{participant.sessionCount:00}",
                participantId = participant.participantId,
                sessionNumber = participant.sessionCount,
                isPostTestSession = isPostTest,
                startUtc = DateTime.UtcNow.ToString("o"),
                appVersion = Application.version,
                deviceModel = SystemInfo.deviceModel,
            };
            SaveSession(CurrentSession);
            return CurrentSession;
        }

        public void SaveSession(SessionRecord session)
        {
            var folder = Path.Combine(ParticipantFolder(session.participantId), "sessions");
            WriteJsonAtomic(Path.Combine(folder, session.sessionId + ".json"), session, "sessions", session.sessionId);
        }

        public void EndSession()
        {
            if (CurrentSession == null)
                return;
            CurrentSession.endUtc = DateTime.UtcNow.ToString("o");
            SaveSession(CurrentSession);
        }

        public List<SessionRecord> LoadSessions(string participantId)
        {
            var folder = Path.Combine(ParticipantFolder(participantId, create: false), "sessions");
            if (!Directory.Exists(folder))
                return new List<SessionRecord>();
            return Directory.GetFiles(folder, "*.json").Select(ReadJson<SessionRecord>).Where(s => s != null)
                .OrderBy(s => s.sessionNumber).ToList();
        }

        // ----------------------------------------------------------------- attempts

        public string NewAttemptId(ParticipantRecord participant) =>
            $"{participant.participantId}-{DateTime.UtcNow:yyyyMMddHHmmss}-{UnityEngine.Random.Range(100, 999)}";

        public void SaveAttempt(AttemptRecord attempt)
        {
            var folder = Path.Combine(ParticipantFolder(attempt.participantId), "attempts");
            WriteJsonAtomic(Path.Combine(folder, attempt.attemptId + ".json"), attempt, "attempts", attempt.attemptId);

            var list = GetAttempts(attempt.participantId);
            list.RemoveAll(a => a.attemptId == attempt.attemptId);
            list.Add(attempt);

            if (CurrentSession != null && CurrentSession.participantId == attempt.participantId && !CurrentSession.attemptIds.Contains(attempt.attemptId))
            {
                CurrentSession.attemptIds.Add(attempt.attemptId);
                SaveSession(CurrentSession);
            }
        }

        /// <summary>All attempts for a participant, oldest first (cached).</summary>
        public List<AttemptRecord> GetAttempts(string participantId)
        {
            if (m_AttemptCache.TryGetValue(participantId, out var cached))
                return cached;

            var list = new List<AttemptRecord>();
            var folder = Path.Combine(ParticipantFolder(participantId, create: false), "attempts");
            if (Directory.Exists(folder))
                list.AddRange(Directory.GetFiles(folder, "*.json").Select(ReadJson<AttemptRecord>).Where(a => a != null));
            list.Sort((a, b) => string.CompareOrdinal(a.startUtc, b.startUtc));
            m_AttemptCache[participantId] = list;
            return list;
        }

        /// <summary>Scored (non-tutorial, completed or collided) attempts for FR9 comparison, oldest first.</summary>
        public List<AttemptRecord> GetScoredAttempts(string participantId, string scenarioId, Perspective perspective) =>
            GetAttempts(participantId)
                .Where(a => a.scenarioId == scenarioId && a.perspective == perspective && a.phase != AttemptPhase.Tutorial && a.endReason != AttemptEndReason.Aborted)
                .ToList();

        public string TelemetryPath(string participantId, string attemptId)
        {
            var folder = Path.Combine(ParticipantFolder(participantId), "telemetry");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, attemptId + ".ndjson");
        }

        // ----------------------------------------------------------------- helpers

        string ParticipantFolder(string participantId, bool create = true)
        {
            var folder = Path.Combine(ParticipantsFolder, participantId);
            if (create)
                Directory.CreateDirectory(folder);
            return folder;
        }

        string GenerateParticipantId()
        {
            var rng = new System.Random(Guid.NewGuid().GetHashCode());
            var existing = new HashSet<string>(ListParticipantIds());
            string id;
            do
            {
                var chars = new char[6];
                for (var i = 0; i < chars.Length; i++)
                    chars[i] = k_IdAlphabet[rng.Next(k_IdAlphabet.Length)];
                id = "RR-" + new string(chars);
            } while (existing.Contains(id));

            return id;
        }

        void WriteJsonAtomic(string path, object model, string collection, string id)
        {
            var json = JsonUtility.ToJson(model, true);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + ".tmp";
            try
            {
                File.WriteAllText(temp, json);
                if (File.Exists(path))
                    File.Replace(temp, path, null);
                else
                    File.Move(temp, path);
            }
            catch (Exception e)
            {
                // File.Replace is not supported on every file system; fall back to a direct write.
                Debug.LogWarning($"[RoadReady] Atomic write failed for {path} ({e.Message}); writing directly.");
                File.WriteAllText(path, json);
                if (File.Exists(temp))
                    File.Delete(temp);
            }

            RecordSaved?.Invoke(collection, id, json);
        }

        static T ReadJson<T>(string path) where T : class
        {
            try
            {
                return File.Exists(path) ? JsonUtility.FromJson<T>(File.ReadAllText(path)) : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RoadReady] Could not read {path}: {e.Message}");
                return null;
            }
        }
    }
}
