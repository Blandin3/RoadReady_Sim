using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using Firebase.Firestore;

namespace RoadReady.Accounts
{
    /// <summary>
    /// Everything about the driving school's own login: register, log in, log out, and "type your password again".
    /// Firebase keeps the login between launches by itself, so the teacher stays logged in on the headset until
    /// they press "Log out school".
    /// </summary>
    public static class AuthService
    {
        const int k_MinPasswordLength = 6; // Firebase's own minimum

        static bool s_Ready;

        /// <summary>Call once before anything else (every scene may call it; later calls return at once).</summary>
        public static async Task EnsureReadyAsync()
        {
            if (s_Ready)
                return;
            var status = await FirebaseApp.CheckAndFixDependenciesAsync();
            if (status != DependencyStatus.Available)
                throw new AccountException("Firebase is not ready on this device (" + status + "). Check the setup steps in the README.");
            s_Ready = true;
        }

        static FirebaseAuth Auth => FirebaseAuth.DefaultInstance;
        static FirebaseFirestore Db => FirebaseFirestore.DefaultInstance;

        public static bool IsLoggedIn => s_Ready && Auth.CurrentUser != null;

        /// <summary>The school's id: the same as the owner's Firebase uid.</summary>
        public static string SchoolId => Auth.CurrentUser?.UserId;

        /// <summary>Creates the Firebase login and the schools/{uid} document, then leaves the teacher logged in.</summary>
        public static async Task RegisterSchoolAsync(string schoolName, string email, string password)
        {
            schoolName = (schoolName ?? "").Trim();
            email = (email ?? "").Trim();
            if (schoolName.Length == 0)
                throw new AccountException("Please type the school name.");
            if (email.Length == 0)
                throw new AccountException("Please type an email address.");
            if (string.IsNullOrEmpty(password) || password.Length < k_MinPasswordLength)
                throw new AccountException($"The password needs at least {k_MinPasswordLength} characters.");

            await EnsureReadyAsync();
            FirebaseUser user;
            try
            {
                var result = await Auth.CreateUserWithEmailAndPasswordAsync(email, password);
                user = result.User;
            }
            catch (Exception e)
            {
                throw Friendly(e);
            }

            try
            {
                // nextCode starts at 1: the first member becomes R-001.
                await Db.Collection("schools").Document(user.UserId).SetAsync(new Dictionary<string, object>
                {
                    { "name", schoolName },
                    { "ownerUid", user.UserId },
                    { "createdAt", FieldValue.ServerTimestamp },
                    { "nextCode", 1 },
                });
            }
            catch (Exception e)
            {
                // Without its school document the login would be useless, so undo it and let the teacher try again.
                try { await user.DeleteAsync(); } catch (Exception) { /* nothing more we can do */ }
                throw Friendly(e);
            }
        }

        public static async Task LoginAsync(string email, string password)
        {
            email = (email ?? "").Trim();
            if (email.Length == 0 || string.IsNullOrEmpty(password))
                throw new AccountException("Please type the email and the password.");

            await EnsureReadyAsync();
            try
            {
                await Auth.SignInWithEmailAndPasswordAsync(email, password);
            }
            catch (Exception e)
            {
                throw Friendly(e);
            }
        }

        public static void LogOut()
        {
            if (s_Ready)
                Auth.SignOut();
            CurrentMember.Clear();
        }

        /// <summary>
        /// Asks Firebase whether this password belongs to the logged-in teacher (used before the Dashboard opens).
        /// Returns false for a wrong password; only real problems (no network...) throw.
        /// </summary>
        public static async Task<bool> ReauthenticateAsync(string password)
        {
            var user = Auth.CurrentUser;
            if (user == null || string.IsNullOrEmpty(password))
                return false;
            try
            {
                await user.ReauthenticateAsync(EmailAuthProvider.GetCredential(user.Email, password));
                return true;
            }
            catch (Exception e)
            {
                var friendly = Friendly(e);
                if (friendly.Message == k_WrongPassword)
                    return false;
                throw friendly;
            }
        }

        public static async Task<SchoolRecord> GetSchoolAsync()
        {
            var id = SchoolId;
            if (id == null)
                throw new AccountException("Nobody is logged in.");
            DocumentSnapshot doc;
            try
            {
                doc = await Db.Collection("schools").Document(id).GetSnapshotAsync();
            }
            catch (Exception e)
            {
                throw Friendly(e);
            }

            if (!doc.Exists)
                throw new AccountException("This login has no school record. Log out and register the school again.");
            return new SchoolRecord
            {
                Id = id,
                Name = doc.GetValue<string>("name"),
                NextCode = doc.ContainsField("nextCode") ? doc.GetValue<int>("nextCode") : 1,
                CreatedAt = doc.ContainsField("createdAt") ? doc.GetValue<Timestamp>("createdAt").ToDateTime() : DateTime.UtcNow,
            };
        }

        // ---------------------------------------------------------------- error messages

        const string k_WrongPassword = "The email or password is wrong.";

        /// <summary>Turns a Firebase / network error into a short sentence the teacher can understand.</summary>
        internal static AccountException Friendly(Exception e)
        {
            // Firebase wraps the real error inside AggregateException / other layers.
            while (e is AggregateException agg && agg.InnerException != null)
                e = agg.InnerException;
            if (e is AccountException account)
                return account;

            if (e is FirebaseException firebase)
            {
                switch ((AuthError)firebase.ErrorCode)
                {
                    case AuthError.EmailAlreadyInUse:
                        return new AccountException("That email already has a school account. Use School login instead.");
                    case AuthError.InvalidEmail:
                    case AuthError.MissingEmail:
                        return new AccountException("That email address does not look right.");
                    case AuthError.WeakPassword:
                        return new AccountException($"The password is too weak (at least {k_MinPasswordLength} characters).");
                    case AuthError.WrongPassword:
                    case AuthError.UserNotFound:
                    case AuthError.InvalidCredential:
                        return new AccountException(k_WrongPassword);
                    case AuthError.NetworkRequestFailed:
                        return new AccountException("No internet connection. Check the Wi-Fi and try again.");
                    case AuthError.TooManyRequests:
                        return new AccountException("Too many tries. Wait a minute and try again.");
                }
            }

            if (e.Message != null && e.Message.Contains("PERMISSION_DENIED"))
                return new AccountException("Firestore refused the request. Check that firestore.rules was published (see the README).");
            return new AccountException("Something went wrong: " + e.Message);
        }
    }
}
