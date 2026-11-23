using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RoadReady.Guidance
{
    /// <summary>
    /// Makes made-up training rows: random feature values, labelled by the SAME rule function the game uses
    /// (<see cref="GuidanceRules.Classify"/>), with the same number of rows for every label. These rows are
    /// marked "synthetic" in the CSV so real and synthetic data can always be told apart and counted.
    /// </summary>
    public static class SyntheticGuidanceData
    {
        public struct Row
        {
            public GuidanceFeatures features;
            public GuidanceLabel label;
        }

        // Speed limits seen in the scenarios (km/h).
        static readonly float[] k_Limits = { 20f, 30f, 40f, 50f, 60f, 80f };
        public const float MaxSpeedKmh = 80f;

        /// <summary>
        /// Draws random rows until every label has <paramref name="perClass"/> rows. Returns the rows in random order.
        /// <paramref name="draws"/> reports how many random candidates had to be drawn in total.
        /// Deterministic for a given seed.
        /// </summary>
        public static List<Row> Generate(int perClass, int seed, GuidanceSettings settings, out int draws)
        {
            var classes = Enum.GetValues(typeof(GuidanceLabel)).Length;
            var counts = new int[classes];
            var rows = new List<Row>(perClass * classes);
            var random = new Random(seed);
            var cap = (long)perClass * classes * 5000L; // safety stop if a label can never be produced
            draws = 0;

            var remaining = classes;
            while (remaining > 0 && draws < cap)
            {
                draws++;
                var f = Sample(random);
                var label = GuidanceRules.Classify(f, settings);
                var index = (int)label;
                if (counts[index] >= perClass)
                    continue;
                counts[index]++;
                if (counts[index] == perClass)
                    remaining--;
                rows.Add(new Row { features = f, label = label });
            }

            // Fisher-Yates shuffle so the classes are mixed.
            for (var i = rows.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (rows[i], rows[j]) = (rows[j], rows[i]);
            }

            return rows;
        }

        // Each "something ahead" value is absent about half the time, which is realistic and makes the rare labels common enough.
        static GuidanceFeatures Sample(Random r)
        {
            return new GuidanceFeatures
            {
                speedKmh = Uniform(r, 0f, MaxSpeedKmh),
                speedLimitKmh = k_Limits[r.Next(k_Limits.Length)],
                stopLineDistance = Maybe(r, 0.5f, 60f, GuidanceFeatures.NoneDistance),
                crossingDistance = r.NextDouble() < 0.05 ? 0f : Maybe(r, 0.5f, 60f, GuidanceFeatures.NoneDistance),
                nearestTtc = Maybe(r, 0.5f, 10f, GuidanceFeatures.NoneTtc, 0.6),
                nearestPedestrianDistance = Maybe(r, 0.5f, 60f, GuidanceFeatures.NoneDistance),
                edgeDistance = Maybe(r, 0f, GuidanceFeatures.NoneEdge, GuidanceFeatures.NoneEdge, 0.6),
            };
        }

        static float Uniform(Random r, float min, float max) => min + (float)r.NextDouble() * (max - min);

        static float Maybe(Random r, float min, float max, float none, double chanceNone = 0.5) =>
            r.NextDouble() < chanceNone ? none : Uniform(r, min, max);

        /// <summary>CSV text with the same columns as the real logger (<see cref="GuidanceCsvLogger.Header"/>).</summary>
        public static string ToCsv(IReadOnlyList<Row> rows)
        {
            var sb = new StringBuilder(rows.Count * 80);
            sb.AppendLine(GuidanceCsvLogger.Header);
            for (var i = 0; i < rows.Count; i++)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "synthetic,synthetic,0,{0},{1}", rows[i].features.ToCsv(), rows[i].label));
            return sb.ToString();
        }
    }
}
