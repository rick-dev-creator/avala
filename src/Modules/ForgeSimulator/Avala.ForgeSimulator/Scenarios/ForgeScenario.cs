using System.Text.RegularExpressions;
using Avala.Forges.Contracts;
using Avala.Sdk;

namespace Avala.ForgeSimulator.Scenarios;

internal sealed record HeadFacts(PullRequestLifecycle Lifecycle, Mergeability Mergeability, IReadOnlyList<CheckRun> Checks, IReadOnlyList<Review> Reviews);

internal static partial class ForgeScenario
{
    public const string Green = "green";

    public const string FailingTest = "1 test failed: CalculatorTests.AddsTwoNumbers";

    public static IReadOnlyList<string> Names { get; } = [Green, "ci-fails-once", "ci-always-fails", "changes-requested-once", "conflict-once", "pending", "merged"];

    public static string NameIn(string text) =>
        Tag().Match(text) is { Success: true } found && Names.Contains(found.Groups["name"].Value) ? found.Groups["name"].Value : Green;

    public static HeadFacts Of(string scenario, int head) => scenario switch
    {
        "ci-fails-once" => Facts(head == 0 ? Build(CheckStatus.Failed) : Build(CheckStatus.Passed)),
        "ci-always-fails" => Facts(Build(CheckStatus.Failed)),
        "changes-requested-once" => Facts(Build(CheckStatus.Passed)) with { Reviews = [head == 0 ? ChangesRequested : Approval] },
        "conflict-once" => Facts(Build(CheckStatus.Passed)) with { Mergeability = head == 0 ? Mergeability.Conflicting : Mergeability.Mergeable },
        "pending" => Facts(Build(CheckStatus.Pending)),
        "merged" => Facts(Build(CheckStatus.Passed)) with { Lifecycle = PullRequestLifecycle.Merged },
        _ => Facts(Build(CheckStatus.Passed)),
    };

    private static Review ChangesRequested { get; } = new("1", "reviewer", ReviewVerdict.ChangesRequested, "Please greet the team by name.", string.Empty)
    {
        Comments = [new ReviewComment("GREETING.md", 1, "Name the team here.")],
    };

    private static Review Approval { get; } = new("2", "reviewer", ReviewVerdict.Approved, "Looks good.", string.Empty);

    private static HeadFacts Facts(CheckRun build) => new(PullRequestLifecycle.Open, Mergeability.Mergeable, [build], []);

    private static CheckRun Build(CheckStatus status) =>
        new("build", status, Option<Uri>.None, status == CheckStatus.Failed ? FailingTest : string.Empty);

    [GeneratedRegex(@"\[forge:\s*(?<name>[a-z-]+)\]")]
    private static partial Regex Tag();
}
