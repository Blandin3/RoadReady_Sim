using System;

namespace RoadReady.Accounts
{
    /// <summary>A driving school, as stored in Firestore at schools/{uid}.</summary>
    public class SchoolRecord
    {
        public string Id;          // the owner's Firebase uid, also the document id
        public string Name;
        public DateTime CreatedAt; // UTC
        public int NextCode;       // the number the next new member will get (starts at 1)
    }
}
