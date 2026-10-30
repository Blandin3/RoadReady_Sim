namespace RoadReady.Accounts
{
    /// <summary>
    /// Who is about to train. StartTraining fills this in just before it loads the game scene, so the game (or a
    /// later script) can read who is playing. Nothing in the existing game reads it yet.
    /// </summary>
    public static class CurrentMember
    {
        public static string SchoolId { get; private set; }
        public static string MemberId { get; private set; }
        public static string FullName { get; private set; }
        public static string Code { get; private set; }

        public static bool IsSet => !string.IsNullOrEmpty(MemberId);

        public static void Set(string schoolId, MemberRecord member)
        {
            SchoolId = schoolId;
            MemberId = member.Id;
            FullName = member.FullName;
            Code = member.Code;
        }

        public static void Clear()
        {
            SchoolId = MemberId = FullName = Code = null;
        }
    }
}
