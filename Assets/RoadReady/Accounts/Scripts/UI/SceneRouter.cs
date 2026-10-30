using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoadReady.Accounts
{
    /// <summary>The names of the account scenes, and one place to move between them.</summary>
    public static class SceneRouter
    {
        public const string SchoolSetup = "SchoolSetup";
        public const string Home = "Home";
        public const string MemberRegister = "MemberRegister";
        public const string StartTraining = "StartTraining";
        public const string Dashboard = "Dashboard";

        /// <summary>The driving / pedestrian simulator itself (the first scene of the game).</summary>
        public const string Game = "RR_Bootstrap";

        /// <summary>
        /// Loads a scene. Returns false (and loads nothing) when the scene is missing from Build Settings, so a button
        /// for a screen that is not built yet shows a message instead of an error.
        /// </summary>
        public static bool Go(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogWarning($"[RoadReady Accounts] Scene '{sceneName}' is not in Build Settings.");
                return false;
            }

            SceneManager.LoadScene(sceneName);
            return true;
        }
    }
}
