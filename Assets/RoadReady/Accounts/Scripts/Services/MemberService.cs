using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;

namespace RoadReady.Accounts
{
    /// <summary>
    /// The school's members (learners): register a new one, and list the existing ones. All data lives at
    /// schools/{schoolId}/members. Only the logged-in owner can touch it (see firestore.rules).
    /// </summary>
    public static class MemberService
    {
        public const int MaxNameLength = 79; // the rules accept names shorter than 80 characters

        static FirebaseFirestore Db => FirebaseFirestore.DefaultInstance;

        static DocumentReference SchoolDoc(string schoolId) => Db.Collection("schools").Document(schoolId);
        static CollectionReference Members(string schoolId) => SchoolDoc(schoolId).Collection("members");

        /// <summary>The answer to <see cref="RegisterAsync"/>.</summary>
        public class RegisterResult
        {
            /// <summary>The new member, or (when <see cref="IsExisting"/>) the member who already has this name today.</summary>
            public MemberRecord Member;

            /// <summary>
            /// True when nothing was created because someone with the same name registered today. The screen should ask
            /// "Is that you?" and, if the answer is "No, register a new person", call RegisterAsync again with confirmNew = true.
            /// </summary>
            public bool IsExisting;
        }

        /// <summary>
        /// Registers a new member and gives them the next code (R-001, R-002...). Rejects an empty name and a name that
        /// is too long. If someone with the same name (spaces trimmed, capital letters ignored) registered today and
        /// <paramref name="confirmNew"/> is false, nothing is created: that existing member is returned instead.
        /// </summary>
        public static async Task<RegisterResult> RegisterAsync(string schoolId, string fullName, bool confirmNew = false)
        {
            fullName = CleanName(fullName);
            if (fullName.Length == 0)
                throw new AccountException("Please type the member's full name.");
            if (fullName.Length > MaxNameLength)
                throw new AccountException($"The name is too long (at most {MaxNameLength} characters).");

            try
            {
                // 1. Same name already registered today? (A transaction cannot run queries, so this check comes first.)
                if (!confirmNew)
                {
                    var existing = await FindRegisteredTodayAsync(schoolId, fullName);
                    if (existing != null)
                        return new RegisterResult { Member = existing, IsExisting = true };
                }

                // 2. One transaction: read nextCode, create the member with that code, and add one to nextCode.
                //    If two devices register at the same moment, Firestore retries so they never get the same code.
                var memberRef = Members(schoolId).Document();
                var code = await Db.RunTransactionAsync(async transaction =>
                {
                    var school = await transaction.GetSnapshotAsync(SchoolDoc(schoolId));
                    if (!school.Exists)
                        throw new AccountException("This school has no record. Log out and register the school again.");

                    var number = school.ContainsField("nextCode") ? school.GetValue<int>("nextCode") : 1;
                    var newCode = FormatCode(number);
                    transaction.Set(memberRef, new Dictionary<string, object>
                    {
                        { "fullName", fullName },
                        { "code", newCode },
                        { "createdAt", FieldValue.ServerTimestamp },
                        { "status", "active" },
                    });
                    transaction.Update(SchoolDoc(schoolId), "nextCode", number + 1);
                    return newCode;
                });

                return new RegisterResult
                {
                    Member = new MemberRecord { Id = memberRef.Id, FullName = fullName, Code = code, CreatedAt = DateTime.UtcNow, Status = "active" },
                };
            }
            catch (Exception e)
            {
                throw AuthService.Friendly(e);
            }
        }

        /// <summary>Trims the name and squeezes runs of spaces into one: "  Jane   Doe " becomes "Jane Doe".</summary>
        public static string CleanName(string name) =>
            string.Join(" ", (name ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));

        /// <summary>The member registered today with this name (capital letters ignored), or null.</summary>
        static async Task<MemberRecord> FindRegisteredTodayAsync(string schoolId, string fullName)
        {
            // Asks only for members created since local midnight (one field, so Firestore needs no extra index),
            // then compares the names here because Firestore cannot ignore capital letters.
            var startOfToday = Timestamp.FromDateTime(DateTime.Now.Date.ToUniversalTime());
            var today = await Members(schoolId).WhereGreaterThanOrEqualTo("createdAt", startOfToday).GetSnapshotAsync();
            foreach (var doc in today.Documents)
            {
                var member = ReadMember(doc);
                if (string.Equals(CleanName(member.FullName), fullName, StringComparison.OrdinalIgnoreCase))
                    return member;
            }

            return null;
        }

        /// <summary>All of the school's members, newest first.</summary>
        public static async Task<List<MemberRecord>> ListAsync(string schoolId)
        {
            try
            {
                var snapshot = await Members(schoolId).OrderByDescending("createdAt").GetSnapshotAsync();
                var list = new List<MemberRecord>();
                foreach (var doc in snapshot.Documents)
                    list.Add(ReadMember(doc));
                return list;
            }
            catch (Exception e)
            {
                throw AuthService.Friendly(e);
            }
        }

        /// <summary>"R-" plus the number padded to three digits: 7 becomes R-007, 1234 stays R-1234.</summary>
        public static string FormatCode(int number) => "R-" + number.ToString("000");

        // ---------------------------------------------------------------- counts for the Dashboard

        public static int CountToday(IEnumerable<MemberRecord> members, DateTime now)
        {
            var count = 0;
            foreach (var m in members)
                if (m.RegisteredOn(now))
                    count++;
            return count;
        }

        /// <summary>Members registered in the last seven days, today included.</summary>
        public static int CountLast7Days(IEnumerable<MemberRecord> members, DateTime now)
        {
            var from = now.Date.AddDays(-6);
            var count = 0;
            foreach (var m in members)
                if (m.CreatedAt.ToLocalTime().Date >= from)
                    count++;
            return count;
        }

        static MemberRecord ReadMember(DocumentSnapshot doc) => new MemberRecord
        {
            Id = doc.Id,
            FullName = doc.GetValue<string>("fullName"),
            Code = doc.GetValue<string>("code"),
            Status = doc.ContainsField("status") ? doc.GetValue<string>("status") : "active",
            // A just-written server timestamp can be empty for a moment on this device: treat that as "now".
            CreatedAt = doc.ContainsField("createdAt") ? doc.GetValue<Timestamp>("createdAt").ToDateTime() : DateTime.UtcNow,
        };
    }
}
