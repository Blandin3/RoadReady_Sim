using System;
using System.Linq;
using NUnit.Framework;
using RoadReady.Config;
using RoadReady.Core;
using RoadReady.Guidance;
using UnityEngine;

namespace RoadReady.Tests
{
    /// <summary>The rule-based driving guide: labels, priority, cue timing, modes, texts and the synthetic data.</summary>
    public class GuidanceTests
    {
        static GuidanceSettings Settings() => new GuidanceSettings();

        // A driver doing 40 in a 50 zone with nothing around: the "all clear" row that each test changes one part of.
        static GuidanceFeatures Calm() => new GuidanceFeatures
        {
            speedKmh = 40f,
            speedLimitKmh = 50f,
            stopLineDistance = GuidanceFeatures.NoneDistance,
            crossingDistance = GuidanceFeatures.NoneDistance,
            nearestTtc = GuidanceFeatures.NoneTtc,
            nearestPedestrianDistance = GuidanceFeatures.NoneDistance,
            edgeDistance = GuidanceFeatures.NoneEdge,
        };

        // ------------------------------------------------------------ labels

        [Test]
        public void CalmDriving_IsSafe() =>
            Assert.AreEqual(GuidanceLabel.Safe, GuidanceRules.Classify(Calm(), Settings()));

        [Test]
        public void SpeedOverTheLimit_IsSlowDown_ButOnlyBeyondTheMargin()
        {
            var f = Calm();
            f.speedKmh = 52f; // exactly 2 over: not yet
            Assert.AreEqual(GuidanceLabel.Safe, GuidanceRules.Classify(f, Settings()));
            f.speedKmh = 52.5f;
            Assert.AreEqual(GuidanceLabel.SlowDown, GuidanceRules.Classify(f, Settings()));
        }

        [Test]
        public void CloseCollisionCourse_IsSlowDown()
        {
            var f = Calm();
            f.nearestTtc = 1.5f;
            Assert.AreEqual(GuidanceLabel.SlowDown, GuidanceRules.Classify(f, Settings()));
            f.nearestTtc = 2.5f;
            Assert.AreEqual(GuidanceLabel.Safe, GuidanceRules.Classify(f, Settings()));
        }

        [Test]
        public void FastNearAPedestrian_IsSlowDown_ButSlowIsFine()
        {
            var f = Calm();
            f.nearestPedestrianDistance = 8f;
            f.speedKmh = 30f;
            Assert.AreEqual(GuidanceLabel.SlowDown, GuidanceRules.Classify(f, Settings()));
            f.speedKmh = 15f;
            Assert.AreEqual(GuidanceLabel.Safe, GuidanceRules.Classify(f, Settings()));
        }

        [Test]
        public void ApproachingACrossing_IsCrossingAhead_NotWhenInsideOrFarAway()
        {
            var f = Calm();
            f.crossingDistance = 20f;
            Assert.AreEqual(GuidanceLabel.CrossingAhead, GuidanceRules.Classify(f, Settings()));
            f.crossingDistance = 0f; // already on it
            Assert.AreEqual(GuidanceLabel.Safe, GuidanceRules.Classify(f, Settings()));
            f.crossingDistance = 45f;
            Assert.AreEqual(GuidanceLabel.Safe, GuidanceRules.Classify(f, Settings()));
        }

        [Test]
        public void StopLineThatDemandsAStop_IsStopAhead()
        {
            var f = Calm();
            f.stopLineDistance = 25f;
            Assert.AreEqual(GuidanceLabel.StopAhead, GuidanceRules.Classify(f, Settings()));
            f.stopLineDistance = 60f;
            Assert.AreEqual(GuidanceLabel.Safe, GuidanceRules.Classify(f, Settings()));
        }

        [Test]
        public void NearTheRoadEdge_IsLaneDeparture()
        {
            var f = Calm();
            f.edgeDistance = 0.4f;
            Assert.AreEqual(GuidanceLabel.LaneDeparture, GuidanceRules.Classify(f, Settings()));
            f.edgeDistance = 1.2f;
            Assert.AreEqual(GuidanceLabel.Safe, GuidanceRules.Classify(f, Settings()));
        }

        [Test]
        public void ACarStandingStill_GetsNoCues()
        {
            var f = Calm();
            f.speedKmh = 0f;
            f.stopLineDistance = 5f;
            f.edgeDistance = 0.2f;
            f.crossingDistance = 5f;
            Assert.AreEqual(GuidanceLabel.Safe, GuidanceRules.Classify(f, Settings()));
        }

        [Test]
        public void WhenSeveralApply_PriorityIsStopThenEdgeThenCrossingThenSlowDown()
        {
            var f = Calm();
            f.speedKmh = 70f; // speeding
            Assert.AreEqual(GuidanceLabel.SlowDown, GuidanceRules.Classify(f, Settings()));
            f.crossingDistance = 20f;
            Assert.AreEqual(GuidanceLabel.CrossingAhead, GuidanceRules.Classify(f, Settings()));
            f.edgeDistance = 0.3f;
            Assert.AreEqual(GuidanceLabel.LaneDeparture, GuidanceRules.Classify(f, Settings()));
            f.stopLineDistance = 20f;
            Assert.AreEqual(GuidanceLabel.StopAhead, GuidanceRules.Classify(f, Settings()));
        }

        [Test]
        public void ThresholdsComeFromTheSettings()
        {
            var settings = Settings();
            settings.stopAheadMetres = 10f;
            var f = Calm();
            f.stopLineDistance = 25f;
            Assert.AreEqual(GuidanceLabel.Safe, GuidanceRules.Classify(f, settings));
        }

        [Test]
        public void LabelNumbers_AreFixed_BecauseTheModelUsesThem()
        {
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, new[]
            {
                (int)GuidanceLabel.Safe, (int)GuidanceLabel.SlowDown, (int)GuidanceLabel.CrossingAhead,
                (int)GuidanceLabel.StopAhead, (int)GuidanceLabel.LaneDeparture,
            });
        }

        // ------------------------------------------------------------ scheduler (when a cue is spoken)

        // Feeds one label every 0.1 s from "from" to "to" and returns the times at which a cue came out.
        static System.Collections.Generic.List<float> Run(GuidanceScheduler s, Func<float, GuidanceLabel> labelAt, float from, float to)
        {
            var cues = new System.Collections.Generic.List<float>();
            for (var t = from; t <= to + 0.001f; t += 0.1f)
                if (s.Update(labelAt(t), t) != GuidanceLabel.Safe)
                    cues.Add(t);
            return cues;
        }

        [Test]
        public void NoCueInTheFirstSeconds()
        {
            var s = new GuidanceScheduler(Settings());
            s.Reset(0f);
            Assert.IsEmpty(Run(s, t => GuidanceLabel.StopAhead, 0f, 4.8f));
        }

        [Test]
        public void ALabelMustHoldBeforeItIsSpoken_AFlickerIsIgnored()
        {
            var s = new GuidanceScheduler(Settings());
            s.Reset(0f);
            // Alternates every 0.1 s: never holds for 0.3 s.
            var flicker = Run(s, t => Mathf.RoundToInt(t * 10f) % 2 == 0 ? GuidanceLabel.SlowDown : GuidanceLabel.Safe, 6f, 9f);
            Assert.IsEmpty(flicker);

            s.Update(GuidanceLabel.Safe, 9.5f); // road clear again, then SlowDown starts at 10.0 and holds
            var steady = Run(s, t => GuidanceLabel.SlowDown, 10f, 10.5f);
            Assert.AreEqual(1, steady.Count);
            Assert.AreEqual(10.3f, steady[0], 0.11f);
        }

        [Test]
        public void TheSameCueIsNotRepeatedWithinTheCooldown()
        {
            var s = new GuidanceScheduler(Settings());
            s.Reset(0f);
            var cues = Run(s, t => GuidanceLabel.SlowDown, 6f, 30f);

            Assert.GreaterOrEqual(cues.Count, 3);
            for (var i = 1; i < cues.Count; i++)
                Assert.GreaterOrEqual(cues[i] - cues[i - 1], 7.9f, "8 s cooldown");
        }

        [Test]
        public void OnlyOneCueAtATime_ALessUrgentCueWaitsItsTurn()
        {
            var s = new GuidanceScheduler(Settings());
            s.Reset(0f);
            // StopAhead first, then SlowDown becomes true 1 s later: SlowDown must wait until the 3 s gap has passed.
            var all = Run(s, t => t < 7f ? GuidanceLabel.StopAhead : GuidanceLabel.SlowDown, 6f, 12f);

            Assert.AreEqual(2, all.Count);
            Assert.GreaterOrEqual(all[1] - all[0], 2.9f);
        }

        [Test]
        public void AMoreUrgentCueMayInterruptWithinTheGap()
        {
            var s = new GuidanceScheduler(Settings());
            s.Reset(0f);
            var all = Run(s, t => t < 6.8f ? GuidanceLabel.SlowDown : GuidanceLabel.StopAhead, 6f, 8f);

            Assert.AreEqual(2, all.Count, "SlowDown, then StopAhead straight away");
            Assert.Less(all[1] - all[0], 2f);
        }

        [Test]
        public void SafeSpeaksNothing()
        {
            var s = new GuidanceScheduler(Settings());
            s.Reset(0f);
            Assert.IsEmpty(Run(s, t => GuidanceLabel.Safe, 0f, 60f));
        }

        // ------------------------------------------------------------ modes and texts

        // The admin override lives in PlayerPrefs, shared with the Editor's Play mode: remember it and put it back
        // so running the tests never wipes a choice saved by hand.
        int m_SavedTraining, m_SavedTest;

        [SetUp]
        public void RememberOverrides()
        {
            m_SavedTraining = PlayerPrefs.GetInt(GuidanceSettings.TrainingPrefsKey, -1);
            m_SavedTest = PlayerPrefs.GetInt(GuidanceSettings.TestPrefsKey, -1);
        }

        [TearDown]
        public void RestoreOverrides()
        {
            GuidanceSettings.ClearModeOverrides();
            if (m_SavedTraining >= 0) PlayerPrefs.SetInt(GuidanceSettings.TrainingPrefsKey, m_SavedTraining);
            if (m_SavedTest >= 0) PlayerPrefs.SetInt(GuidanceSettings.TestPrefsKey, m_SavedTest);
        }

        [Test]
        public void Modes_DefaultToCoachingInTrainingAndWarningsOnlyInTests()
        {
            GuidanceSettings.ClearModeOverrides();
            var s = Settings();
            Assert.AreEqual(GuidanceMode.Coaching, s.ModeFor(AttemptPhase.Tutorial));
            Assert.AreEqual(GuidanceMode.Coaching, s.ModeFor(AttemptPhase.Training));
            Assert.AreEqual(GuidanceMode.WarningsOnly, s.ModeFor(AttemptPhase.PreTest));
            Assert.AreEqual(GuidanceMode.WarningsOnly, s.ModeFor(AttemptPhase.PostTest));
        }

        [Test]
        public void TheAdminOverride_WinsOverTheAsset_AndCanBeCleared()
        {
            var s = Settings();
            GuidanceSettings.SaveModeOverride(true, GuidanceMode.Off);
            GuidanceSettings.SaveModeOverride(false, GuidanceMode.WarningsOnly);
            Assert.AreEqual(GuidanceMode.Off, s.ModeFor(AttemptPhase.PostTest));
            Assert.AreEqual(GuidanceMode.WarningsOnly, s.ModeFor(AttemptPhase.Training));

            GuidanceSettings.ClearModeOverrides();
            Assert.AreEqual(GuidanceMode.Coaching, s.ModeFor(AttemptPhase.Training));
        }

        [Test]
        public void EveryCueHasATextInBothModes_AndCoachingIsLongerThanAWarning()
        {
            foreach (var label in new[] { GuidanceLabel.SlowDown, GuidanceLabel.CrossingAhead, GuidanceLabel.StopAhead, GuidanceLabel.LaneDeparture })
            {
                var warning = GuidanceCues.Key(label, GuidanceMode.WarningsOnly);
                var coach = GuidanceCues.Key(label, GuidanceMode.Coaching);
                Assert.AreNotEqual(warning, InstructionLibrary.Get(warning), label + " warning text is missing");
                Assert.AreNotEqual(coach, InstructionLibrary.Get(coach), label + " coaching text is missing");
                Assert.Greater(InstructionLibrary.Get(coach).Length, InstructionLibrary.Get(warning).Length);
            }

            Assert.IsNull(GuidanceCues.Key(GuidanceLabel.Safe, GuidanceMode.Coaching));
        }

        // ------------------------------------------------------------ synthetic data

        [Test]
        public void SyntheticData_HasTheSameNumberOfRowsForEveryLabel()
        {
            var rows = SyntheticGuidanceData.Generate(200, 1, Settings(), out _);

            Assert.AreEqual(1000, rows.Count);
            foreach (GuidanceLabel label in Enum.GetValues(typeof(GuidanceLabel)))
                Assert.AreEqual(200, rows.Count(r => r.label == label), label.ToString());
        }

        [Test]
        public void SyntheticLabels_AreExactlyWhatTheRulesSay()
        {
            var settings = Settings();
            var rows = SyntheticGuidanceData.Generate(100, 2, settings, out _);

            foreach (var row in rows)
                Assert.AreEqual(GuidanceRules.Classify(row.features, settings), row.label);
        }

        [Test]
        public void SyntheticData_IsRepeatableForASeed_AndDiffersBetweenSeeds()
        {
            var a = SyntheticGuidanceData.Generate(50, 7, Settings(), out _);
            var b = SyntheticGuidanceData.Generate(50, 7, Settings(), out _);
            var c = SyntheticGuidanceData.Generate(50, 8, Settings(), out _);

            Assert.AreEqual(SyntheticGuidanceData.ToCsv(a), SyntheticGuidanceData.ToCsv(b));
            Assert.AreNotEqual(SyntheticGuidanceData.ToCsv(a), SyntheticGuidanceData.ToCsv(c));
        }

        [Test]
        public void SyntheticValues_StayInTheirRanges()
        {
            foreach (var row in SyntheticGuidanceData.Generate(100, 3, Settings(), out _))
            {
                var f = row.features;
                Assert.That(f.speedKmh, Is.InRange(0f, SyntheticGuidanceData.MaxSpeedKmh));
                Assert.That(f.stopLineDistance, Is.InRange(0.5f, GuidanceFeatures.NoneDistance));
                Assert.That(f.crossingDistance, Is.InRange(0f, GuidanceFeatures.NoneDistance));
                Assert.That(f.nearestTtc, Is.InRange(0.5f, GuidanceFeatures.NoneTtc));
                Assert.That(f.nearestPedestrianDistance, Is.InRange(0.5f, GuidanceFeatures.NoneDistance));
                Assert.That(f.edgeDistance, Is.InRange(0f, GuidanceFeatures.NoneEdge));
            }
        }

        [Test]
        public void CsvRows_HaveTheSameColumnsAsTheHeader_AndMarkTheirSource()
        {
            var csv = SyntheticGuidanceData.ToCsv(SyntheticGuidanceData.Generate(10, 4, Settings(), out _));
            var lines = csv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var columns = lines[0].Split(',').Length;

            Assert.AreEqual(GuidanceCsvLogger.Header, lines[0]);
            Assert.AreEqual(51, lines.Length);
            foreach (var line in lines.Skip(1))
            {
                var cells = line.Split(',');
                Assert.AreEqual(columns, cells.Length);
                Assert.AreEqual("synthetic", cells[0]);
                Assert.IsTrue(Enum.IsDefined(typeof(GuidanceLabel), cells[columns - 1]));
            }
        }

        [Test]
        public void FeatureCsv_UsesADotNotACommaForDecimals()
        {
            var f = Calm();
            f.speedKmh = 12.5f;
            var cells = f.ToCsv().Split(',');

            Assert.AreEqual(GuidanceFeatures.CsvHeader.Split(',').Length, cells.Length);
            Assert.AreEqual("12.5", cells[0]);
        }
    }
}
