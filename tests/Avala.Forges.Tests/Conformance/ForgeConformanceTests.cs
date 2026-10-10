using Avala.Forges.Contracts;
using Avala.Forges.Testing;
using Avala.Sdk;

namespace Avala.Forges.Tests.Conformance;

public sealed class ForgeConformanceTests
{
    private const string Head = "avala/job";

    private static readonly PullRequestRef Opened = new(7, new Uri("https://forge.invalid/octo/shop/pulls/7"), Head, "main");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AForgeThatCannotFindOrOpenIsReportedAndCheckedNoFurtherAsync()
    {
        var forge = new MisbehavingForge
        {
            Finds = new Queue<Result<Option<PullRequestRef>, ForgeError>>([ForgeError.Unreachable]),
            Opening = ForgeError.Rejected,
        };

        Assert.Equal(["finding before opening failed with Unreachable", "opening failed with Rejected"], await CheckAsync(forge));
    }

    [Fact]
    public async Task AForgeThatAnswersAgainstTheContractIsReportedForEveryBreachAsync()
    {
        var forge = new MisbehavingForge
        {
            Info = new ForgeInfo(string.Empty, "Broken"),
            Finds = new Queue<Result<Option<PullRequestRef>, ForgeError>>([Option<PullRequestRef>.Some(Opened), Option<PullRequestRef>.None]),
            Opening = Opened with { Number = 0 },
            Reading = new PullRequestState(Opened with { Number = 8 }, PullRequestLifecycle.Closed, string.Empty, Mergeability.Unknown)
            {
                Checks = [new CheckRun(string.Empty, CheckStatus.Passed, Option<Uri>.None, string.Empty)],
                Reviews = [new Review(string.Empty, "hubot", ReviewVerdict.Approved, string.Empty, string.Empty)],
            },
            Commenting = new CommentRef(string.Empty, Option<Uri>.None),
        };

        Assert.Equal(
            [
                "the forge declares no id, name or token scheme",
                "found a pull request before one was opened",
                $"the opened pull request is not the one asked for: {Opened with { Number = 0 }}",
                "the opened pull request is not found again",
                "a fresh pull request is not open",
                "the pull request has no head commit",
                "reading returned another pull request",
                "a check has no name",
                "a review has no id or author",
                "the comment has no id",
                "an unknown pull request is not NotFound",
            ],
            await CheckAsync(forge));
    }

    [Fact]
    public async Task AForgeWhoseCallsFailOnceThePullRequestIsOpenIsReportedForEachAsync()
    {
        var forge = new MisbehavingForge
        {
            Finds = new Queue<Result<Option<PullRequestRef>, ForgeError>>([Option<PullRequestRef>.None, ForgeError.RateLimited]),
            Reading = ForgeError.Unauthorized,
            Commenting = ForgeError.Rejected,
        };

        Assert.Equal(
            ["finding after opening failed with RateLimited", "reading failed with Unauthorized", "commenting failed with Rejected", "an unknown pull request is not NotFound"],
            await CheckAsync(forge));
    }

    private static Task<IReadOnlyList<string>> CheckAsync(MisbehavingForge forge) =>
        ForgeConformance.CheckAsync(
            new ConformanceCase(forge, new ForgeContext(new Unused(), new ForgeTarget(new Uri("https://forge.invalid"), new RepositoryAddress("forge.invalid", "octo", "shop"), "origin")), Head, "main"),
            Cancellation);

    private sealed class MisbehavingForge : IForge
    {
        public ForgeInfo Info { get; init; } = new("broken", "Broken");

        public Queue<Result<Option<PullRequestRef>, ForgeError>> Finds { get; init; } = new();

        public Result<PullRequestRef, ForgeError> Opening { get; init; } = Opened;

        public Result<PullRequestState, ForgeError> Reading { get; init; } = new PullRequestState(Opened, PullRequestLifecycle.Open, "6dcb09b", Mergeability.Mergeable);

        public Result<CommentRef, ForgeError> Commenting { get; init; } = new CommentRef("1", Option<Uri>.None);

        public Option<CliCall> CliFor(ForgeRequest request, ForgeTarget target) => Option<CliCall>.None;

        public ValueTask<Result<Option<PullRequestRef>, ForgeError>> FindAsync(ForgeContext context, string head, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Finds.Dequeue());

        public ValueTask<Result<PullRequestRef, ForgeError>> OpenAsync(ForgeContext context, PullRequestDraft draft, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Opening);

        public ValueTask<Result<PullRequestState, ForgeError>> ReadAsync(ForgeContext context, PullRequestRef pullRequest, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Reading);

        public ValueTask<Result<CommentRef, ForgeError>> CommentAsync(ForgeContext context, PullRequestRef pullRequest, string body, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Commenting);
    }

    private sealed class Unused : IForgeApi
    {
        public ValueTask<Result<ForgeResponse, ForgeError>> SendAsync(ForgeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
