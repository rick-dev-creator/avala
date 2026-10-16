using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;

namespace Avala.Workbench.Presenting;

internal static class DelegationPhrases
{
    public const string NoneYet = "No job has delegated work yet";

    public const string NoneShown = "No orchestration matches these filters";

    public static string Caption(IReadOnlyList<Overview.OrchestrationViewModel> shown)
    {
        var working = shown.Sum(tree => tree.Children.Count(child => !child.IsSettled));
        var needing = shown.Sum(tree => tree.Children.Count(child => child.NeedsYou));

        return string.Join(" · ", new[]
        {
            Overview.OverviewPhrases.Count(shown.Count, "orchestration"),
            working == 0 ? string.Empty : $"{working} at work",
            needing == 0 ? string.Empty : $"{needing} needs you",
        }.Where(part => part.Length > 0));
    }

    public static string Role(ChildRole role) => role switch
    {
        ChildRole.Reviewer => "reviewer, read-only",
        ChildRole.Research => "research, read-only",
        _ => string.Empty,
    };

    public static string Status(JobStatus status) => status switch
    {
        JobStatus.Draft => "draft",
        JobStatus.Preparing => "starting",
        JobStatus.Running => "running",
        JobStatus.Checking => "checking",
        JobStatus.NeedsHelp => "needs help",
        JobStatus.AwaitingReview => "ready for review",
        JobStatus.Approved => "approved",
        JobStatus.Discarded => "discarded",
        _ => "failed",
    };

    public static string Outcome(ChildOutcome outcome) => outcome switch
    {
        ChildOutcome.Integrated => "integrated into its parent",
        ChildOutcome.Conflict => "conflicts with its parent",
        ChildOutcome.NotIntegrated => "not integrated",
        ChildOutcome.Held => "held",
        ChildOutcome.RetriesExhausted => "retries ran out",
        ChildOutcome.Failed => "failed",
        ChildOutcome.Reported => "reported its findings",
        _ => "discarded",
    };

    public static string Refusal(DelegationError error) => error switch
    {
        DelegationError.NotDeclared => "the repository declares no delegation to that connection",
        DelegationError.DepthExceeded => "too deep",
        DelegationError.TooManyChildren => "too many children",
        DelegationError.AutonomyLoosened => "it asked for more autonomy than its parent",
        DelegationError.RoleLoosened => "it asked for a worker where children are read-only",
        DelegationError.NotSubmitted => "the child job was refused",
        DelegationError.UnofferedModel => "its connection does not offer the model it asked for",
        DelegationError.UnofferedEffort => "its connection does not offer the effort it asked for",
        DelegationError.MalformedInput => "the call was malformed",
        DelegationError.NoJob => "the caller is not a job",
        _ => "the delegation rules cannot be read",
    };
}
