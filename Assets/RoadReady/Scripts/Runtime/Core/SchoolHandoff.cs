using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoadReady.Core
{
    /// <summary>
    /// Who is training, handed from the school Home screens (the Accounts scenes) to the game. When a learner arrives
    /// this way the app skips the splash and the name page (the name is already known) and goes straight to Welcome;
    /// at the end of the session it returns to the school Home. Played directly from the bootstrap scene, nothing is set
    /// and the app starts as before.
    /// </summary>
    public static class SchoolHandoff
    {
        public const string HomeScene = "Home";

        public static string LearnerName { get; private set; }
        public static string LearnerCode { get; private set; }

        public static bool HasLearner => !string.IsNullOrEmpty(LearnerName);

        public static bool CanReturnHome => HasLearner && Application.CanStreamedLevelBeLoaded(HomeScene);

        public static void Set(string name, string code)
        {
            LearnerName = name;
            LearnerCode = code;
        }

        public static void Clear()
        {
            LearnerName = LearnerCode = null;
        }

        /// <summary>Loads the school Home scene. Returns false when nobody came from there (or the scene is not in Build Settings).</summary>
        public static bool ReturnHome()
        {
            if (!CanReturnHome)
                return false;
            Time.timeScale = 1f;
            SceneManager.LoadScene(HomeScene);
            return true;
        }
    }
}
