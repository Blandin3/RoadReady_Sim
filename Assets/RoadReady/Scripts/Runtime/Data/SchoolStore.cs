using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace RoadReady.Data
{
    [Serializable]
    class SchoolAccount
    {
        public string username;
        public string salt;
        public string hash;
    }

    [Serializable]
    public class AttendanceEntry
    {
        public string name;
        public string utc;

        public DateTime Local => DateTime.TryParse(utc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime() : DateTime.MinValue;
    }

    [Serializable]
    class AttendanceBook
    {
        public List<AttendanceEntry> entries = new List<AttendanceEntry>();
    }

    /// <summary>
    /// The one driving-school account and the list of learners who used the experience. Only the school can log in
    /// (the password is kept as a salted hash); a learner just types a name, which is added to the day's list.
    /// Kept in the "school" folder, apart from the study data. Layout under persistentDataPath/RoadReady/school:
    /// <code>
    ///   account.json      school username + salted password hash
    ///   attendance.json   learner name + time, one entry per visit
    /// </code>
    /// </summary>
    public class SchoolStore
    {
        public const int MinPasswordLength = 4;

        readonly string m_Folder;
        SchoolAccount m_Account;
        AttendanceBook m_Book;

        public SchoolStore(string rootFolder) => m_Folder = Path.Combine(rootFolder, "school");

        string AccountPath => Path.Combine(m_Folder, "account.json");
        string BookPath => Path.Combine(m_Folder, "attendance.json");

        public bool HasAccount => Account != null;
        public string Username => Account?.username;

        /// <summary>First run only: the school picks its username and password. Returns false if an account already exists.</summary>
        public bool CreateAccount(string username, string password)
        {
            if (HasAccount || string.IsNullOrWhiteSpace(username) || password == null || password.Length < MinPasswordLength)
                return false;
            var salt = Convert.ToBase64String(RandomBytes(16));
            m_Account = new SchoolAccount { username = username.Trim(), salt = salt, hash = Hash(salt, password) };
            Directory.CreateDirectory(m_Folder);
            File.WriteAllText(AccountPath, JsonUtility.ToJson(m_Account, true));
            return true;
        }

        public bool Verify(string username, string password)
        {
            var account = Account;
            if (account == null || username == null || password == null)
                return false;
            return string.Equals(username.Trim(), account.username, StringComparison.OrdinalIgnoreCase) && Hash(account.salt, password) == account.hash;
        }

        public AttendanceEntry AddLearner(string name)
        {
            var entry = new AttendanceEntry { name = name.Trim(), utc = DateTime.UtcNow.ToString("o") };
            Book.entries.Add(entry);
            Directory.CreateDirectory(m_Folder);
            File.WriteAllText(BookPath, JsonUtility.ToJson(Book, true));
            return entry;
        }

        /// <summary>The learners who started on <paramref name="day"/> (local time), first to last.</summary>
        public List<AttendanceEntry> LearnersOn(DateTime day)
        {
            var list = Book.entries.FindAll(e => e.Local.Date == day.Date);
            list.Sort((a, b) => string.CompareOrdinal(a.utc, b.utc));
            return list;
        }

        SchoolAccount Account
        {
            get
            {
                if (m_Account != null)
                    return m_Account;
                try
                {
                    m_Account = File.Exists(AccountPath) ? JsonUtility.FromJson<SchoolAccount>(File.ReadAllText(AccountPath)) : null;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[RoadReady] Could not read the school account: {e.Message}");
                }

                return m_Account;
            }
        }

        AttendanceBook Book
        {
            get
            {
                if (m_Book != null)
                    return m_Book;
                try
                {
                    m_Book = File.Exists(BookPath) ? JsonUtility.FromJson<AttendanceBook>(File.ReadAllText(BookPath)) : null;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[RoadReady] Could not read the learner list: {e.Message}");
                }

                return m_Book ??= new AttendanceBook();
            }
        }

        static byte[] RandomBytes(int count)
        {
            var bytes = new byte[count];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return bytes;
        }

        static string Hash(string salt, string password)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(salt + ":" + password)));
        }
    }
}
