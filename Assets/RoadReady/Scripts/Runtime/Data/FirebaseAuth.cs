using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace RoadReady.Data
{
    /// <summary>
    /// Firebase Authentication over its REST API (no SDK to install): each headset signs in anonymously once and
    /// keeps its refresh token, so the Realtime Database can require a signed-in app without shipping a secret.
    /// The Web API key is not a secret; access is controlled by the database rules.
    /// </summary>
    public class FirebaseAuth
    {
        [Serializable]
        class SignUpResponse
        {
            public string idToken;
            public string refreshToken;
            public string expiresIn;
            public string localId;
        }

        [Serializable]
        class RefreshResponse
        {
            public string id_token;
            public string refresh_token;
            public string expires_in;
            public string user_id;
        }

        [Serializable]
        class ErrorResponse
        {
            [Serializable]
            public class Error
            {
                public string message;
            }

            public Error error;
        }

        const string k_PrefsKey = "RoadReady.FirebaseRefreshToken";

        readonly string m_ApiKey;
        string m_RefreshToken;
        DateTime m_ExpiresUtc;

        public string IdToken { get; private set; }
        public string UserId { get; private set; }
        public string LastError { get; private set; }
        public bool HasValidToken => !string.IsNullOrEmpty(IdToken) && DateTime.UtcNow < m_ExpiresUtc.AddMinutes(-2);

        public FirebaseAuth(string apiKey, bool rememberSignIn = true)
        {
            m_ApiKey = apiKey.Trim();
            m_RefreshToken = rememberSignIn ? PlayerPrefs.GetString(k_PrefsKey, "") : "";
        }

        /// <summary>Signs in (or refreshes the sign-in) when needed. Check <see cref="HasValidToken"/> afterwards.</summary>
        public IEnumerator EnsureToken()
        {
            if (HasValidToken)
                yield break;
            using var request = BuildTokenRequest();
            yield return request.SendWebRequest();
            HandleTokenResponse(request);
        }

        public UnityWebRequest BuildTokenRequest()
        {
            UnityWebRequest request;
            if (!string.IsNullOrEmpty(m_RefreshToken))
            {
                var body = "grant_type=refresh_token&refresh_token=" + UnityWebRequest.EscapeURL(m_RefreshToken);
                request = new UnityWebRequest($"https://securetoken.googleapis.com/v1/token?key={m_ApiKey}", UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                    downloadHandler = new DownloadHandlerBuffer(),
                };
                request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
            }
            else
            {
                request = new UnityWebRequest($"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={m_ApiKey}", UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{\"returnSecureToken\":true}")),
                    downloadHandler = new DownloadHandlerBuffer(),
                };
                request.SetRequestHeader("Content-Type", "application/json");
            }

            request.timeout = 20;
            return request;
        }

        /// <summary>Reads a finished <see cref="BuildTokenRequest"/>. Returns true when signed in.</summary>
        public bool HandleTokenResponse(UnityWebRequest request)
        {
            var refreshing = !string.IsNullOrEmpty(m_RefreshToken);
            if (request.result != UnityWebRequest.Result.Success)
            {
                LastError = Explain(request);
                if (refreshing)
                {
                    // A revoked or expired refresh token: forget it so the next attempt signs in afresh.
                    m_RefreshToken = "";
                    PlayerPrefs.DeleteKey(k_PrefsKey);
                }

                return false;
            }

            var text = request.downloadHandler.text;
            string expiresIn;
            if (refreshing)
            {
                var response = JsonUtility.FromJson<RefreshResponse>(text);
                IdToken = response.id_token;
                m_RefreshToken = response.refresh_token;
                UserId = response.user_id;
                expiresIn = response.expires_in;
            }
            else
            {
                var response = JsonUtility.FromJson<SignUpResponse>(text);
                IdToken = response.idToken;
                m_RefreshToken = response.refreshToken;
                UserId = response.localId;
                expiresIn = response.expiresIn;
            }

            m_ExpiresUtc = DateTime.UtcNow.AddSeconds(int.TryParse(expiresIn, out var seconds) ? seconds : 3600);
            PlayerPrefs.SetString(k_PrefsKey, m_RefreshToken ?? "");
            PlayerPrefs.Save();
            LastError = null;
            return !string.IsNullOrEmpty(IdToken);
        }

        /// <summary>Turns Firebase's error codes into the fix to make in the Firebase console.</summary>
        static string Explain(UnityWebRequest request)
        {
            var code = "";
            try
            {
                code = JsonUtility.FromJson<ErrorResponse>(request.downloadHandler?.text ?? "")?.error?.message ?? "";
            }
            catch (ArgumentException)
            {
            }

            if (code.Contains("ADMIN_ONLY_OPERATION") || code.Contains("OPERATION_NOT_ALLOWED"))
                return "Sign-in refused: enable 'Anonymous' in Firebase console > Authentication > Sign-in method.";
            if (code.Contains("API key not valid") || code.Contains("API_KEY_INVALID"))
                return "The Web API key is wrong: copy it again from Firebase console > Project settings > General.";
            if (request.result == UnityWebRequest.Result.ConnectionError)
                return "No internet connection (" + request.error + ").";
            return $"Sign-in failed: {request.responseCode} {(string.IsNullOrEmpty(code) ? request.error : code)}";
        }
    }
}
