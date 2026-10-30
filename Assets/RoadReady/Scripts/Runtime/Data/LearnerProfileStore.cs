using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RoadReady.Data
{
    [Serializable]
    public class LearnerLink
    {
        public string key;           // the member code (R-001) or the typed name, lower case
        public string participantId; // the anonymous study record that belongs to that person
    }

    [Serializable]
    class LearnerBook
    {
        public List<LearnerLink> links = new List<LearnerLink>();
    }

    /// <summary>
    /// Remembers which study record belongs to which learner, so the details chosen on the participant setup page
    /// (group, condition, age band, experience) are filled in for them next time and only need approving or editing.
    /// Kept in its own file, apart from the study data: a participant record never holds a name. It is removed
    /// together with the participant on withdrawal.
    /// </summary>
    public class LearnerProfileStore
    {
        readonly string m_Path;
        LearnerBook m_Book;

        public LearnerProfileStore(string rootFolder) => m_Path = Path.Combine(rootFolder, "learners.json");

        /// <summary>The key for a learner: their school member code when they have one, otherwise their name (case and spaces ignored).</summary>
        public static string MakeKey(string name, string code)
        {
            if (!string.IsNullOrWhiteSpace(code))
                return code.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(name))
                return null;
            return string.Join(" ", name.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        }

        public string FindParticipantId(string key) =>
            key == null ? null : Book.links.Find(l => l.key == key)?.participantId;

        public void Link(string key, string participantId)
        {
            if (key == null || participantId == null)
                return;
            var link = Book.links.Find(l => l.key == key);
            if (link == null)
                Book.links.Add(link = new LearnerLink { key = key });
            link.participantId = participantId;
            Write();
        }

        public void Remove(string participantId)
        {
            if (Book.links.RemoveAll(l => l.participantId == participantId) > 0)
                Write();
        }

        LearnerBook Book
        {
            get
            {
                if (m_Book != null)
                    return m_Book;
                try
                {
                    m_Book = File.Exists(m_Path) ? JsonUtility.FromJson<LearnerBook>(File.ReadAllText(m_Path)) : null;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[RoadReady] Could not read the learner profiles: {e.Message}");
                }

                return m_Book ??= new LearnerBook();
            }
        }

        void Write()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(m_Path));
            File.WriteAllText(m_Path, JsonUtility.ToJson(Book, true));
        }
    }
}
