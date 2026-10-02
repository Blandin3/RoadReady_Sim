using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using RoadReady.Config;
using UnityEngine;
using UnityEngine.Networking;

namespace RoadReady.Data
{
    /// <summary>
    /// Optional cloud backup of study records (proposal 3.6: Firebase / PostgreSQL). Records are queued in an
    /// on-disk outbox and uploaded when the headset has connectivity, so data collection never depends on
    /// the pilot site's Wi-Fi. Remote layout: participants/{pid}/{collection}/{id}.
    /// </summary>
    public class RemoteSync : MonoBehaviour
    {
        const float k_RetrySeconds = 15f;
        const int k_ParticipantIdLength = 9; // "RR-" + 6

        RoadReadyConfig m_Config;
        DataService m_Data;
        string m_Outbox;
        bool m_Busy;

        public int PendingCount => Directory.Exists(m_Outbox) ? Directory.GetFiles(m_Outbox, "*.json").Length : 0;
        public string LastError { get; private set; }
        public bool Enabled => m_Config != null && m_Config.remoteBackend != RemoteBackend.None && !string.IsNullOrEmpty(m_Config.remoteBaseUrl);

        public void Initialize(RoadReadyConfig config, DataService data)
        {
            m_Config = config;
            m_Data = data;
            m_Outbox = Path.Combine(data.RootFolder, "outbox");
            Directory.CreateDirectory(m_Outbox);
            data.RecordSaved += OnRecordSaved;
            data.ParticipantDeleted += OnParticipantDeleted;
            StartCoroutine(UploadLoop());
        }

        void OnDestroy()
        {
            if (m_Data == null)
                return;
            m_Data.RecordSaved -= OnRecordSaved;
            m_Data.ParticipantDeleted -= OnParticipantDeleted;
        }

        void OnRecordSaved(string collection, string id, string json)
        {
            if (!Enabled)
                return;
            File.WriteAllText(Path.Combine(m_Outbox, $"put__{collection}__{id}.json"), json);
        }

        void OnParticipantDeleted(string participantId)
        {
            if (!Directory.Exists(m_Outbox))
                return;
            // Drop anything still queued for this participant, then queue the remote delete.
            foreach (var file in Directory.GetFiles(m_Outbox, "*.json").Where(f => Path.GetFileName(f).Contains(participantId)))
                File.Delete(file);
            if (Enabled)
                File.WriteAllText(Path.Combine(m_Outbox, $"delete__participants__{participantId}.json"), "{}");
        }

        IEnumerator UploadLoop()
        {
            var wait = new WaitForSecondsRealtime(k_RetrySeconds);
            while (true)
            {
                if (Enabled && !m_Busy && Application.internetReachability != NetworkReachability.NotReachable)
                    yield return FlushOutbox();
                yield return wait;
            }
        }

        IEnumerator FlushOutbox()
        {
            m_Busy = true;
            foreach (var file in Directory.GetFiles(m_Outbox, "*.json").OrderBy(File.GetLastWriteTimeUtc))
            {
                var parts = Path.GetFileNameWithoutExtension(file).Split(new[] { "__" }, System.StringSplitOptions.None);
                if (parts.Length != 3)
                {
                    File.Delete(file);
                    continue;
                }

                var (verb, collection, id) = (parts[0], parts[1], parts[2]);
                using var request = BuildRequest(verb, collection, id, verb == "put" ? File.ReadAllText(file) : null);
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    File.Delete(file);
                    LastError = null;
                }
                else
                {
                    LastError = $"{request.responseCode} {request.error}";
                    Debug.LogWarning($"[RoadReady] Upload of {collection}/{id} failed: {LastError}");
                    break; // retry on next loop
                }
            }

            m_Busy = false;
        }

        UnityWebRequest BuildRequest(string verb, string collection, string id, string json)
        {
            var pid = id.Length >= k_ParticipantIdLength ? id.Substring(0, k_ParticipantIdLength) : id;
            var baseUrl = m_Config.remoteBaseUrl.TrimEnd('/');
            var remotePath = collection == "participants" ? $"participants/{pid}/profile" : $"participants/{pid}/{collection}/{id}";
            if (verb == "delete")
                remotePath = $"participants/{pid}";

            string url;
            if (m_Config.remoteBackend == RemoteBackend.FirebaseRealtimeDatabase)
            {
                url = $"{baseUrl}/{remotePath}.json";
                if (!string.IsNullOrEmpty(m_Config.remoteAuthToken))
                    url += "?auth=" + UnityWebRequest.EscapeURL(m_Config.remoteAuthToken);
            }
            else
            {
                url = $"{baseUrl}/{remotePath}";
            }

            UnityWebRequest request;
            if (verb == "delete")
            {
                request = UnityWebRequest.Delete(url);
            }
            else
            {
                var method = m_Config.remoteBackend == RemoteBackend.FirebaseRealtimeDatabase ? UnityWebRequest.kHttpVerbPUT : UnityWebRequest.kHttpVerbPOST;
                request = new UnityWebRequest(url, method)
                {
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                    downloadHandler = new DownloadHandlerBuffer(),
                };
                request.SetRequestHeader("Content-Type", "application/json");
            }

            if (m_Config.remoteBackend == RemoteBackend.JsonPost && !string.IsNullOrEmpty(m_Config.remoteAuthToken))
                request.SetRequestHeader("Authorization", "Bearer " + m_Config.remoteAuthToken);
            request.timeout = 20;
            return request;
        }
    }
}
