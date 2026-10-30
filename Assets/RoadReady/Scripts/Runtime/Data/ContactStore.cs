using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace RoadReady.Data
{
    [Serializable]
    public class ContactEntry
    {
        public string participantId;
        public string name;
        public string email;
        public string reportFile;
        public string reportUtc;
    }

    [Serializable]
    class ContactBook
    {
        public List<ContactEntry> entries = new List<ContactEntry>();
    }

    /// <summary>
    /// Names and e-mail addresses that participants choose to leave so the researcher can send them their results
    /// report. Kept in the "reports" folder, apart from the study data: it is never part of a participant record, an
    /// export or a remote sync, and it is removed together with the participant on withdrawal.
    /// Layout under persistentDataPath/RoadReady/reports:
    /// <code>
    ///   contacts.json        who left what (read by the app)
    ///   recipients.csv       name, email, report file (open this to send the reports)
    ///   {pid}_{name}.pdf     one overall report per person, rewritten after every attempt
    /// </code>
    /// </summary>
    public class ContactStore
    {
        readonly string m_Folder;
        ContactBook m_Book;

        public ContactStore(string rootFolder) => m_Folder = Path.Combine(rootFolder, "reports");

        public string Folder => m_Folder;
        string BookPath => Path.Combine(m_Folder, "contacts.json");
        string CsvPath => Path.Combine(m_Folder, "recipients.csv");

        public ContactEntry Get(string participantId) =>
            participantId == null ? null : Book.entries.Find(e => e.participantId == participantId);

        public static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;
            var at = email.IndexOf('@');
            return at > 0 && at == email.LastIndexOf('@') && email.IndexOf('.', at) > at + 1 && !email.EndsWith(".") && !email.Contains(" ");
        }

        public ContactEntry Save(string participantId, string name, string email)
        {
            var entry = Get(participantId);
            if (entry == null)
            {
                entry = new ContactEntry { participantId = participantId };
                Book.entries.Add(entry);
            }

            entry.name = name.Trim();
            entry.email = email.Trim();
            Write();
            return entry;
        }

        public void MarkReport(ContactEntry entry, string file)
        {
            entry.reportFile = file;
            entry.reportUtc = DateTime.UtcNow.ToString("o");
            Write();
        }

        /// <summary>Withdrawal: forget the contact and delete the report.</summary>
        public void Remove(string participantId)
        {
            var entry = Get(participantId);
            if (entry == null)
                return;
            if (!string.IsNullOrEmpty(entry.reportFile))
            {
                var path = Path.Combine(m_Folder, entry.reportFile);
                if (File.Exists(path))
                    File.Delete(path);
            }

            Book.entries.Remove(entry);
            Write();
        }

        ContactBook Book
        {
            get
            {
                if (m_Book != null)
                    return m_Book;
                try
                {
                    m_Book = File.Exists(BookPath) ? JsonUtility.FromJson<ContactBook>(File.ReadAllText(BookPath)) : null;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[RoadReady] Could not read the report contacts: {e.Message}");
                }

                return m_Book ??= new ContactBook();
            }
        }

        void Write()
        {
            Directory.CreateDirectory(m_Folder);
            File.WriteAllText(BookPath, JsonUtility.ToJson(Book, true));

            var csv = new StringBuilder("name,email,participant,report_file,report_updated_utc\n");
            foreach (var e in Book.entries)
                csv.Append(Cell(e.name)).Append(',').Append(Cell(e.email)).Append(',').Append(Cell(e.participantId)).Append(',')
                    .Append(Cell(e.reportFile)).Append(',').Append(Cell(e.reportUtc)).Append('\n');
            File.WriteAllText(CsvPath, csv.ToString());
        }

        static string Cell(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }
}
