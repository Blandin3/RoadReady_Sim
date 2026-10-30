using System;
using System.Text;
using RoadReady.Config;
using RoadReady.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// RoadReady > Database > Test Connection: checks the Firebase settings in RoadReadyConfig end to end - signs in
    /// anonymously, writes a test record, reads it back and deletes it - and says exactly what to fix if a step fails.
    /// Runs without entering Play mode.
    /// </summary>
    public static class DatabaseConnectionTest
    {
        enum Step { SignIn, Write, Read, Delete }

        static FirebaseAuth s_Auth;
        static UnityWebRequest s_Request;
        static Step s_Step;
        static string s_Url;
        static string s_Stamp;
        static Action<bool, string> s_Done;

        [MenuItem("RoadReady/Database/Test Connection", priority = 50)]
        public static void RunFromMenu() => Run((ok, message) =>
            EditorUtility.DisplayDialog("RoadReady database", message, "OK"));

        /// <summary>Batch-mode entry point: logs the result and exits (0 = connected).</summary>
        public static void RunBatch() => Run((ok, message) => EditorApplication.Exit(ok ? 0 : 1));

        static void Run(Action<bool, string> done)
        {
            s_Done = done;
            var config = AssetDatabase.LoadAssetAtPath<RoadReadyConfig>(GreyboxKit.ConfigPath);
            var problem = Validate(config);
            if (problem != null)
            {
                Finish(false, problem);
                return;
            }

            s_Url = config.remoteBaseUrl.Trim().TrimEnd('/');
            s_Auth = new FirebaseAuth(config.remoteApiKey, rememberSignIn: false);
            Start(Step.SignIn, s_Auth.BuildTokenRequest());
        }

        static string Validate(RoadReadyConfig config)
        {
            if (config == null)
                return "RoadReadyConfig not found. Run RoadReady > Build Everything first.";
            if (config.remoteBackend != RemoteBackend.FirebaseRealtimeDatabase)
                return "In RoadReadyConfig, set 'Remote Backend' to 'Firebase Realtime Database'.";
            var url = config.remoteBaseUrl?.Trim() ?? "";
            if (!url.StartsWith("https://") || !(url.Contains(".firebaseio.com") || url.Contains(".firebasedatabase.app")))
                return "In RoadReadyConfig, 'Remote Base Url' must be your Realtime Database URL, e.g.\nhttps://roadready-vr-default-rtdb.europe-west1.firebasedatabase.app\n(Firebase console > Realtime Database > Data tab, top of the page).";
            if (string.IsNullOrWhiteSpace(config.remoteApiKey))
                return "In RoadReadyConfig, paste the 'Web API Key' into 'Remote Api Key'\n(Firebase console > Project settings > General).";
            return null;
        }

        static void Start(Step step, UnityWebRequest request)
        {
            s_Step = step;
            s_Request = request;
            s_Request.SendWebRequest();
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
            EditorUtility.DisplayProgressBar("RoadReady database", step + "...", ((int)step + 1) / 5f);
        }

        static void Poll()
        {
            if (s_Request == null || !s_Request.isDone)
                return;
            EditorApplication.update -= Poll;
            var request = s_Request;
            s_Request = null;
            try
            {
                Advance(request);
            }
            finally
            {
                request.Dispose();
            }
        }

        static void Advance(UnityWebRequest request)
        {
            switch (s_Step)
            {
                case Step.SignIn:
                    if (!s_Auth.HandleTokenResponse(request))
                    {
                        Finish(false, s_Auth.LastError);
                        return;
                    }

                    s_Stamp = DateTime.UtcNow.ToString("o");
                    var json = $"{{\"testedUtc\":\"{s_Stamp}\",\"from\":\"Unity editor\"}}";
                    var put = new UnityWebRequest(TestUrl(), UnityWebRequest.kHttpVerbPUT)
                    {
                        uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                        downloadHandler = new DownloadHandlerBuffer(),
                    };
                    put.SetRequestHeader("Content-Type", "application/json");
                    Start(Step.Write, put);
                    return;

                case Step.Write:
                    if (!Ok(request, "write"))
                        return;
                    Start(Step.Read, UnityWebRequest.Get(TestUrl()));
                    return;

                case Step.Read:
                    if (!Ok(request, "read back"))
                        return;
                    if (!request.downloadHandler.text.Contains(s_Stamp))
                    {
                        Finish(false, "The test record was written but came back different. Check the database URL points at the right project.");
                        return;
                    }

                    Start(Step.Delete, UnityWebRequest.Delete(TestUrl()));
                    return;

                case Step.Delete:
                    if (!Ok(request, "delete"))
                        return;
                    Finish(true, "Connected. Signed in, wrote, read back and deleted a test record.\n\n" +
                                 "Participants, sessions and attempts will now upload to Firebase under 'participants/'.");
                    return;
            }
        }

        static string TestUrl() => $"{s_Url}/connection_tests/{s_Auth.UserId}.json?auth={UnityWebRequest.EscapeURL(s_Auth.IdToken)}";

        static bool Ok(UnityWebRequest request, string what)
        {
            if (request.result == UnityWebRequest.Result.Success)
                return true;
            string message;
            if (request.responseCode == 401 || request.responseCode == 403)
                message = $"Signed in, but the database refused to {what} (permission denied).\nPaste the RoadReady rules into Firebase console > Realtime Database > Rules and press Publish.";
            else if (request.responseCode == 404)
                message = "Database not found: check 'Remote Base Url' (copy it from Realtime Database > Data).";
            else if (request.result == UnityWebRequest.Result.ConnectionError)
                message = $"Could not reach the database ({request.error}). Check the URL and your internet connection.";
            else
                message = $"Could not {what} the test record: {request.responseCode} {request.error}\n{request.downloadHandler?.text}";
            Finish(false, message);
            return false;
        }

        static void Finish(bool ok, string message)
        {
            EditorUtility.ClearProgressBar();
            if (ok) Debug.Log("[RoadReady] Database test: " + message);
            else Debug.LogError("[RoadReady] Database test failed: " + message);
            s_Done?.Invoke(ok, message);
        }
    }
}
