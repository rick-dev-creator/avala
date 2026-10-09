namespace Avala.Autopilot.Evidence;

internal enum ApprovalRule
{
    Never,
    CleanEvidence,
}

internal enum FollowUpRule
{
    Refuse,
    Accept,
}

internal sealed record AutopilotRules(ApprovalRule Approve, FollowUpRule FollowUps)
{
    public static AutopilotRules Default { get; } = new(ApprovalRule.Never, FollowUpRule.Refuse);
}
