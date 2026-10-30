using System;

namespace RoadReady.Accounts
{
    /// <summary>One learner, as stored in Firestore at schools/{uid}/members/{autoId}.</summary>
    public class MemberRecord
    {
        public string Id;          // the auto-generated document id
        public string FullName;
        public string Code;        // R-001, R-002, ... (tells two people with the same name apart)
        public DateTime CreatedAt; // UTC
        public string Status;      // always "active" for now

        /// <summary>How the member is shown in lists: "Full Name (R-007)".</summary>
        public string Label => $"{FullName} ({Code})";

        /// <summary>True when the member registered on the same calendar day (device local time) as <paramref name="now"/>.</summary>
        public bool RegisteredOn(DateTime now) => CreatedAt.ToLocalTime().Date == now.Date;
    }
}
