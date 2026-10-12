using System.Globalization;
using System.Text.Json;
using Avala.ForgeSimulator.Scenarios;
using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Triggers.Contracts;

namespace Avala.ForgeSimulator.Remote;

internal sealed record SimulatedPull(int Number, string Head, string Base, string Title, string Body, string Scenario)
{
    public List<string> Heads { get; init; } = [];

    public List<string> Comments { get; init; } = [];
}

internal sealed record SimulatedRemote
{
    public List<SimulatedPull> Pulls { get; init; } = [];

    public List<SimulatedIssue> Issues { get; init; } = [];

    public List<SimulatedEvent> Events { get; init; } = [];

    public List<SimulatedFailure> FailingHeads { get; init; } = [];
}

internal sealed class SimulatedForge(IProcessRunner processes, AvalaPaths paths) : IForge, IForgeEvents, IForgeRepositories, IForgeAccounts, IAsyncDisposable
{
    public const string Id = "simulated";

    public const string StateFile = "avala-forge.json";

    public static Uri Site { get; } = new("https://forge.invalid");

    private readonly SerialExecutor executor = new();

    public ForgeInfo Info { get; } = new(Id, "Simulated forge") { DefaultUrl = Site };

    public HookSignature Signature { get; } = new(["X-Hub-Signature-256"], "sha256=");

    public Option<CliCall> CliFor(ForgeRequest request, ForgeTarget target) => Option<CliCall>.None;

    public string GitHost(Uri api) => string.Empty;

    public async ValueTask<Result<HostedRepository, ForgeError>> CreateAsync(ForgeContext context, RepositoryDraft draft, CancellationToken cancellationToken) =>
        await executor.RunAsync(token => new SimulatedRepositories(processes, paths).CreateAsync(draft, token), cancellationToken);

    public async ValueTask<Result<IReadOnlyList<ListedRepository>, ForgeError>> ListAsync(ForgeContext context, CancellationToken cancellationToken) =>
        await executor.RunAsync(_ => Task.FromResult(Result<IReadOnlyList<ListedRepository>, ForgeError>.Success(new SimulatedRepositories(processes, paths).List())), cancellationToken);

    public ValueTask<Result<ForgeAccount, ForgeError>> MeAsync(ForgeContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<ForgeAccount, ForgeError>.Success(new ForgeAccount(SimulatedRepositories.Owner)));

    public async ValueTask<Result<Option<PullRequestRef>, ForgeError>> FindAsync(ForgeContext context, string head, CancellationToken cancellationToken) =>
        await executor.RunAsync(
            async token => (await LoadAsync(context, token)).Map(remote => remote.Pulls.Where(pull => pull.Head == head).Select(pull => Reference(context, pull)).FirstOrDefault().ToOption()),
            cancellationToken);

    public async ValueTask<Result<PullRequestRef, ForgeError>> OpenAsync(ForgeContext context, PullRequestDraft draft, CancellationToken cancellationToken) =>
        await executor.RunAsync(
            async token =>
            {
                if (!(await LoadAsync(context, token)).TryGetValue(out var remote, out var error))
                {
                    return error;
                }

                if (!(await HeadAsync(context, draft.Head, token)).IsSuccess || !(await HeadAsync(context, draft.Base, token)).IsSuccess)
                {
                    return ForgeError.Rejected;
                }

                var pull = new SimulatedPull(remote.Pulls.Count + 1, draft.Head, draft.Base, draft.Title, draft.Body, ForgeScenario.NameIn($"{draft.Title}\n{draft.Body}"));
                remote.Pulls.Add(pull);
                await SaveAsync(context, remote, token);

                return Result<PullRequestRef, ForgeError>.Success(Reference(context, pull));
            },
            cancellationToken);

    public async ValueTask<Result<PullRequestState, ForgeError>> ReadAsync(ForgeContext context, PullRequestRef pullRequest, CancellationToken cancellationToken) =>
        await executor.RunAsync(
            async token =>
            {
                if (!(await LoadAsync(context, token)).TryGetValue(out var remote, out var error))
                {
                    return error;
                }

                if (remote.Pulls.Find(pull => pull.Number == pullRequest.Number) is not { } pull)
                {
                    return ForgeError.NotFound;
                }

                if (!(await HeadAsync(context, pull.Head, token)).TryGetValue(out var head, out error))
                {
                    return error;
                }

                if (!pull.Heads.Contains(head))
                {
                    pull.Heads.Add(head);
                    await SaveAsync(context, remote, token);
                }

                var facts = ForgeScenario.Of(pull.Scenario, pull.Heads.IndexOf(head));

                return Result<PullRequestState, ForgeError>.Success(new PullRequestState(Reference(context, pull), facts.Lifecycle, head, facts.Mergeability)
                {
                    Checks = facts.Checks,
                    Reviews = [.. facts.Reviews.Select(review => review with { Commit = head })],
                });
            },
            cancellationToken);

    public async ValueTask<Result<CommentRef, ForgeError>> CommentAsync(ForgeContext context, PullRequestRef pullRequest, string body, CancellationToken cancellationToken) =>
        await executor.RunAsync(
            async token =>
            {
                if (!(await LoadAsync(context, token)).TryGetValue(out var remote, out var error))
                {
                    return error;
                }

                if (remote.Pulls.Find(pull => pull.Number == pullRequest.Number) is not { } pull)
                {
                    return ForgeError.NotFound;
                }

                pull.Comments.Add(body);
                await SaveAsync(context, remote, token);
                var id = pull.Comments.Count.ToString(CultureInfo.InvariantCulture);

                return Result<CommentRef, ForgeError>.Success(new CommentRef(id, new Uri($"{pullRequest.Link}#comment-{id}")));
            },
            cancellationToken);

    public async ValueTask<Result<ForgeEventPage, ForgeError>> ReadSinceAsync(ForgeContext context, ForgeEventQuery query, CancellationToken cancellationToken) =>
        await executor.RunAsync(
            async token =>
            {
                if (!(await LoadAsync(context, token)).TryGetValue(out var remote, out var error))
                {
                    return error;
                }

                if (query.Kind != ForgeEventKind.ChecksFailed)
                {
                    return Result<ForgeEventPage, ForgeError>.Success(SimulatedEvents.Since(remote, query, SiteOf(context)));
                }

                var branch = query.Branch.Match(name => name, () => string.Empty);

                return (await HeadAsync(context, branch, token)).Map(head => SimulatedEvents.Checks(remote, query, branch, head));
            },
            cancellationToken);

    public ValueTask DisposeAsync() => executor.DisposeAsync();

    private static Uri SiteOf(ForgeContext context) => new(Site, $"{context.Target.Repository.Owner}/{context.Target.Repository.Name}/");

    private static PullRequestRef Reference(ForgeContext context, SimulatedPull pull) =>
        new(pull.Number, new Uri(Site, $"{context.Target.Repository.Owner}/{context.Target.Repository.Name}/pulls/{pull.Number.ToString(CultureInfo.InvariantCulture)}"), pull.Head, pull.Base);

    private static string StatePath(ForgeContext context) => Path.Combine(context.Target.Remote, StateFile);

    private static async Task<Result<SimulatedRemote, ForgeError>> LoadAsync(ForgeContext context, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(context.Target.Remote))
        {
            return ForgeError.NotFound;
        }

        var path = StatePath(context);

        if (!File.Exists(path))
        {
            return new SimulatedRemote();
        }

        try
        {
            return JsonSerializer.Deserialize<SimulatedRemote>(await File.ReadAllTextAsync(path, cancellationToken)) is { } remote ? remote : ForgeError.Malformed;
        }
        catch (JsonException)
        {
            return ForgeError.Malformed;
        }
    }

    private static Task SaveAsync(ForgeContext context, SimulatedRemote remote, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(StatePath(context), JsonSerializer.Serialize(remote), cancellationToken);

    private async Task<Result<string, ForgeError>> HeadAsync(ForgeContext context, string branch, CancellationToken cancellationToken) =>
        (await processes.RunAsync(new ProcessRequest("git", ["-C", context.Target.Remote, "rev-parse", "--verify", "--quiet", $"refs/heads/{branch}"]), cancellationToken)).Match(
            outcome => outcome.Succeeded ? Result<string, ForgeError>.Success(outcome.Output.Trim()) : ForgeError.NotFound,
            _ => ForgeError.GitFailed);
}
