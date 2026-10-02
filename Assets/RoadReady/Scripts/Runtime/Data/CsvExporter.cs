using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RoadReady.Core;

namespace RoadReady.Data
{
    /// <summary>
    /// "Export session data" use case: flattens all local records into analysis-ready CSV files
    /// (one row per participant / session / attempt / hazard / decision) plus a pre/post summary that
    /// feeds directly into a paired comparison for RQ3.
    /// </summary>
    public static class CsvExporter
    {
        static readonly CultureInfo k_Inv = CultureInfo.InvariantCulture;

        public static string Export(DataService data)
        {
            var folder = Path.Combine(data.ExportsFolder, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(folder);

            var participants = data.LoadAllParticipants();
            var attempts = participants.SelectMany(p => data.GetAttempts(p.participantId)).ToList();
            var sessions = participants.SelectMany(p => data.LoadSessions(p.participantId)).ToList();

            WriteParticipants(Path.Combine(folder, "participants.csv"), participants);
            WriteSessions(Path.Combine(folder, "sessions.csv"), sessions);
            WriteAttempts(Path.Combine(folder, "attempts.csv"), attempts);
            WriteHazards(Path.Combine(folder, "hazards.csv"), attempts);
            WriteDecisions(Path.Combine(folder, "decisions.csv"), attempts);
            WritePrePost(Path.Combine(folder, "prepost_summary.csv"), participants, attempts);
            return folder;
        }

        static void WriteParticipants(string path, List<ParticipantRecord> rows)
        {
            var sb = new StringBuilder("participant_id,created_utc,group,condition,age_band,driving_experience,consent,consent_utc,guardian_consent,sessions\n");
            foreach (var p in rows)
                Line(sb, p.participantId, p.createdUtc, p.group, p.condition, p.ageBand, p.drivingExperience, p.consentGiven, p.consentUtc, p.guardianConsentVerified, p.sessionCount);
            File.WriteAllText(path, sb.ToString());
        }

        static void WriteSessions(string path, List<SessionRecord> rows)
        {
            var sb = new StringBuilder("session_id,participant_id,session_number,post_test_session,start_utc,end_utc,attempts,ssq_nausea,ssq_oculomotor,ssq_disorientation,ssq_total,sus_score,app_version,device\n");
            foreach (var s in rows)
                Line(sb, s.sessionId, s.participantId, s.sessionNumber, s.isPostTestSession, s.startUtc, s.endUtc, s.attemptIds.Count,
                    s.hasSsq ? F(s.ssq.nausea) : "", s.hasSsq ? F(s.ssq.oculomotor) : "", s.hasSsq ? F(s.ssq.disorientation) : "",
                    s.hasSsq ? F(s.ssq.total) : "", s.hasSus ? F(s.sus.total) : "", s.appVersion, s.deviceModel);
            File.WriteAllText(path, sb.ToString());
        }

        static void WriteAttempts(string path, List<AttemptRecord> rows)
        {
            var sb = new StringBuilder("attempt_id,participant_id,session_id,condition,scenario_id,scenario_type,perspective,level,phase,attempt_number,start_utc,duration_s,end_reason,seed," +
                                       "total_score,grade,hazards_total,hazards_detected,detection_rate,mean_reaction_s,violations,minor,major,critical,safe_decisions,borderline_decisions,unsafe_decisions,collision," +
                                       "perception_failures,comprehension_failures,projection_failures\n");
            foreach (var a in rows)
            {
                var s = a.score;
                Line(sb, a.attemptId, a.participantId, a.sessionId, a.condition, a.scenarioId, a.scenarioType, a.perspective, a.difficultyLevel, a.phase, a.attemptNumber,
                    a.startUtc, F(a.durationSeconds), a.endReason, a.randomSeed,
                    F(s.total), s.grade, s.hazardsTotal, s.hazardsDetected, F(s.detectionRate), F(s.meanReactionTime), s.violationCount, s.minorViolations, s.majorViolations,
                    s.criticalViolations, s.safeDecisions, s.borderlineDecisions, s.unsafeDecisions, s.collision, s.perceptionFailures, s.comprehensionFailures, s.projectionFailures);
            }

            File.WriteAllText(path, sb.ToString());
        }

        static void WriteHazards(string path, List<AttemptRecord> rows)
        {
            var sb = new StringBuilder("record_id,attempt_id,participant_id,scenario_id,perspective,phase,hazard_id,hazard_type,required_response,severity,onset_s,fixation_s,spotted_s,response_s,reaction_s,min_ttc_s,perceived,responded,collided,decision,sa_outcome\n");
            foreach (var a in rows)
            foreach (var h in a.hazardRecords)
                Line(sb, h.recordId, a.attemptId, a.participantId, a.scenarioId, a.perspective, a.phase, h.hazardId, h.hazardType, h.requiredResponse, h.severity,
                    F(h.onsetTime), F(h.fixationTime), F(h.spottedPressTime), F(h.responseTime), F(h.reactionTime), F(h.minTimeToCollision),
                    h.perceived, h.responded, h.collided, h.decision, h.saOutcome);
            File.WriteAllText(path, sb.ToString());
        }

        static void WriteDecisions(string path, List<AttemptRecord> rows)
        {
            var sb = new StringBuilder("decision_id,attempt_id,participant_id,scenario_id,perspective,phase,source,rule_id,title,decision,severity,time_s,x,z,speed_kmh\n");
            foreach (var a in rows)
            foreach (var d in a.decisions)
                Line(sb, d.decisionId, a.attemptId, a.participantId, a.scenarioId, a.perspective, a.phase, d.source, d.ruleId, d.title, d.decision, d.severity,
                    F(d.time), F(d.posX), F(d.posZ), F(d.speedKmh));
            File.WriteAllText(path, sb.ToString());
        }

        static void WritePrePost(string path, List<ParticipantRecord> participants, List<AttemptRecord> attempts)
        {
            var sb = new StringBuilder("participant_id,condition,scenario_id,perspective,pre_attempt_id,post_attempt_id,post_source,training_attempts," +
                                       "pre_score,post_score,delta_score,pre_detection,post_detection,delta_detection,pre_reaction_s,post_reaction_s,delta_reaction_s,pre_violations,post_violations,delta_violations\n");
            foreach (var p in participants)
            {
                var groups = attempts.Where(a => a.participantId == p.participantId && a.phase != AttemptPhase.Tutorial && a.endReason != AttemptEndReason.Aborted)
                    .GroupBy(a => (a.scenarioId, a.perspective));
                foreach (var g in groups)
                {
                    var ordered = g.OrderBy(a => a.startUtc, StringComparer.Ordinal).ToList();
                    var pre = ordered.FirstOrDefault(a => a.phase == AttemptPhase.PreTest) ?? ordered.First();
                    var post = ordered.FirstOrDefault(a => a.phase == AttemptPhase.PostTest);
                    var source = "post_test";
                    if (post == null)
                    {
                        post = ordered.LastOrDefault(a => a != pre);
                        source = post != null ? "last_training" : "none";
                    }

                    var training = ordered.Count(a => a.phase == AttemptPhase.Training);
                    if (post == null)
                    {
                        Line(sb, p.participantId, p.condition, g.Key.scenarioId, g.Key.perspective, pre.attemptId, "", source, training,
                            F(pre.score.total), "", "", F(pre.score.detectionRate), "", "", F(pre.score.meanReactionTime), "", "", pre.score.violationCount, "", "");
                        continue;
                    }

                    Line(sb, p.participantId, p.condition, g.Key.scenarioId, g.Key.perspective, pre.attemptId, post.attemptId, source, training,
                        F(pre.score.total), F(post.score.total), F(post.score.total - pre.score.total),
                        F(pre.score.detectionRate), F(post.score.detectionRate), F(post.score.detectionRate - pre.score.detectionRate),
                        F(pre.score.meanReactionTime), F(post.score.meanReactionTime),
                        pre.score.meanReactionTime >= 0 && post.score.meanReactionTime >= 0 ? F(post.score.meanReactionTime - pre.score.meanReactionTime) : "",
                        pre.score.violationCount, post.score.violationCount, post.score.violationCount - pre.score.violationCount);
                }
            }

            File.WriteAllText(path, sb.ToString());
        }

        static string F(float value) => value.ToString("0.###", k_Inv);

        static void Line(StringBuilder sb, params object[] values)
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var text = values[i] switch
                {
                    null => "",
                    float f => F(f),
                    bool b => b ? "1" : "0",
                    _ => Convert.ToString(values[i], k_Inv),
                };
                if (text.IndexOfAny(new[] { ',', '"', '\n' }) >= 0)
                    text = "\"" + text.Replace("\"", "\"\"") + "\"";
                sb.Append(text);
            }

            sb.Append('\n');
        }
    }
}
