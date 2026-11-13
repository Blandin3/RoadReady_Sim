namespace RoadReady.Guidance
{
    /// <summary>
    /// The rule-based guide. One pure function: numbers in, label out. It is the baseline the game uses today
    /// and the ground truth the decision-tree model is trained to copy.
    /// </summary>
    public static class GuidanceRules
    {
        /// <summary>
        /// Priority when several things are true at once: StopAhead, then LaneDeparture, then CrossingAhead,
        /// then SlowDown, then Safe. (A stop line or leaving the road matters more than speed.)
        /// A car that is standing still gets Safe, because there is nothing to warn about.
        /// </summary>
        public static GuidanceLabel Classify(in GuidanceFeatures f, GuidanceSettings s)
        {
            var moving = f.speedKmh >= s.minMovingSpeedKmh;
            if (!moving)
                return GuidanceLabel.Safe;

            if (f.stopLineDistance > 0f && f.stopLineDistance <= s.stopAheadMetres)
                return GuidanceLabel.StopAhead;

            if (f.edgeDistance <= s.edgeWarningMetres)
                return GuidanceLabel.LaneDeparture;

            // crossingDistance is 0 while already on the crossing: too late for "ahead".
            if (f.crossingDistance > 0f && f.crossingDistance <= s.crossingAheadMetres)
                return GuidanceLabel.CrossingAhead;

            if (f.OverLimitKmh > s.slowDownOverLimitKmh
                || f.nearestTtc < s.warningTtcSeconds
                || (f.nearestPedestrianDistance <= s.pedestrianSlowMetres && f.speedKmh > s.pedestrianSlowSpeedKmh))
                return GuidanceLabel.SlowDown;

            return GuidanceLabel.Safe;
        }

        /// <summary>Higher number = more urgent.</summary>
        public static int Priority(GuidanceLabel label)
        {
            switch (label)
            {
                case GuidanceLabel.StopAhead: return 4;
                case GuidanceLabel.LaneDeparture: return 3;
                case GuidanceLabel.CrossingAhead: return 2;
                case GuidanceLabel.SlowDown: return 1;
                default: return 0;
            }
        }
    }
}
