using System;

namespace RoadReady.Accounts
{
    /// <summary>An error whose message is safe to show to the teacher as it is ("Wrong password", "Name is empty"...).</summary>
    public class AccountException : Exception
    {
        public AccountException(string message) : base(message) { }
    }
}
