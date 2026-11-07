using System;
using NUnit.Framework;
using UnityEngine.TestTools;
using RoadReady.Core;

namespace RoadReady.Tests
{
    /// <summary>The "before leaving" doorbell: the game carries on exactly once, with or without a listener.</summary>
    public class SessionHooksTests
    {
        [TearDown]
        public void TearDown() => SessionHooks.BeforeLeaving = null;

        [Test]
        public void WithNoListener_TheGameCarriesOnAtOnce()
        {
            var runs = 0;

            SessionHooks.RunBeforeLeaving(() => runs++);

            Assert.AreEqual(1, runs);
        }

        [Test]
        public void WithAListener_TheGameWaitsUntilTheListenerIsDone()
        {
            Action carryOn = null;
            SessionHooks.BeforeLeaving = c => carryOn = c; // the listener keeps the action and calls it later
            var runs = 0;

            SessionHooks.RunBeforeLeaving(() => runs++);
            Assert.AreEqual(0, runs, "still waiting");

            carryOn();
            Assert.AreEqual(1, runs);
        }

        [Test]
        public void CarryingOnTwice_OnlyRunsTheGameOnce()
        {
            Action carryOn = null;
            SessionHooks.BeforeLeaving = c => carryOn = c;
            var runs = 0;
            SessionHooks.RunBeforeLeaving(() => runs++);

            carryOn();
            carryOn();

            Assert.AreEqual(1, runs);
        }

        [Test]
        public void IfTheListenerFails_TheGameStillCarriesOn()
        {
            SessionHooks.BeforeLeaving = _ => throw new InvalidOperationException("listener broke");
            var runs = 0;

            LogAssert.Expect(UnityEngine.LogType.Exception, new System.Text.RegularExpressions.Regex("listener broke"));
            SessionHooks.RunBeforeLeaving(() => runs++);

            Assert.AreEqual(1, runs);
        }
    }
}
