namespace RoadReady.Guidance
{
    /// <summary>
    /// Decides WHEN a label is announced: it must hold for a moment, the same cue is not repeated too soon,
    /// only one cue at a time, and a more urgent cue may interrupt a less urgent one. Pure logic (time is passed
    /// in), so it is unit tested.
    /// </summary>
    public sealed class GuidanceScheduler
    {
        const float k_Never = -1000f;

        readonly GuidanceSettings m_Settings;
        readonly float[] m_LastCueAt = new float[5];
        float m_StartedAt;
        float m_LastAnyCueAt = k_Never;
        GuidanceLabel m_LastCue = GuidanceLabel.Safe;
        GuidanceLabel m_Candidate = GuidanceLabel.Safe;
        float m_CandidateSince;

        public GuidanceScheduler(GuidanceSettings settings) => m_Settings = settings;

        public void Reset(float now)
        {
            for (var i = 0; i < m_LastCueAt.Length; i++)
                m_LastCueAt[i] = k_Never;
            m_StartedAt = now;
            m_LastAnyCueAt = k_Never;
            m_LastCue = GuidanceLabel.Safe;
            m_Candidate = GuidanceLabel.Safe;
            m_CandidateSince = now;
        }

        /// <summary>Feed the label measured now. Returns the cue to announce, or Safe when nothing should be said.</summary>
        public GuidanceLabel Update(GuidanceLabel label, float now)
        {
            if (label != m_Candidate)
            {
                m_Candidate = label;
                m_CandidateSince = now;
            }

            if (label == GuidanceLabel.Safe)
                return GuidanceLabel.Safe;
            if (now - m_StartedAt < m_Settings.startDelaySeconds)
                return GuidanceLabel.Safe;
            if (now - m_CandidateSince < m_Settings.confirmSeconds)
                return GuidanceLabel.Safe;
            if (now - m_LastCueAt[(int)label] < m_Settings.cueCooldownSeconds)
                return GuidanceLabel.Safe;

            // One cue at a time: wait for the gap, unless this cue is more urgent than the last one spoken.
            var gapPassed = now - m_LastAnyCueAt >= m_Settings.minGapBetweenCuesSeconds;
            if (!gapPassed && GuidanceRules.Priority(label) <= GuidanceRules.Priority(m_LastCue))
                return GuidanceLabel.Safe;

            m_LastCueAt[(int)label] = now;
            m_LastAnyCueAt = now;
            m_LastCue = label;
            return label;
        }
    }
}
