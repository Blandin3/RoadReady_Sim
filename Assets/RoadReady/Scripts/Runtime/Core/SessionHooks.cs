using System;
using RoadReady.Data;

namespace RoadReady.Core
{
    /// <summary>
    /// Two "doorbells" the game rings so that other code (the end-of-session email in RoadReady.Reports) can react
    /// without the game knowing it exists. The game never references Reports, so there is no circular reference,
    /// and if nobody is listening nothing happens.
    /// </summary>
    public static class SessionHooks
    {
        /// <summary>Rung when a scored attempt finishes (not a tutorial, not an aborted attempt).</summary>
        public static event Action<AttemptRecord> ScoredAttemptFinished;

        /// <summary>
        /// Set by a listener that needs a moment before the learner leaves (the report email). It is called with a
        /// "carry on" action and must call it when it is done, or after a short time limit at the latest.
        /// </summary>
        public static Action<Action> BeforeLeaving;

        public static void RaiseScoredAttempt(AttemptRecord attempt)
        {
            try
            {
                ScoredAttemptFinished?.Invoke(attempt);
            }
            catch (Exception e)
            {
                // A faulty listener must never break the game.
                UnityEngine.Debug.LogException(e);
            }
        }

        /// <summary>
        /// Runs <paramref name="carryOn"/> straight away when nobody is listening, otherwise after the listener
        /// is done. It never runs twice, and it still runs if the listener throws an error.
        /// </summary>
        public static void RunBeforeLeaving(Action carryOn)
        {
            var done = false;

            void Once()
            {
                if (done)
                    return;
                done = true;
                carryOn();
            }

            var listener = BeforeLeaving;
            if (listener == null)
            {
                Once();
                return;
            }

            try
            {
                listener(Once);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
                Once();
            }
        }
    }
}
