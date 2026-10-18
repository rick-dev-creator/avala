using Avala.Documentation.Contracts;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workbench.Board;
using Avala.Workbench.Machine;
using Avala.Workbench.RepositoryRules;
using Avala.Workbench.Submitting;
using Avala.Workbench.Timeline;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Readying;

internal sealed record ReadinessFacts(string Repository, ForgeReadiness Forge, bool RulesUncommitted, bool ChecksMissing);

internal interface IReadinessLinks
{
    Task DeclareChecksAsync(string repository);

    void OpenSettings();
}

internal sealed class FirstJobs(IJobCatalog catalog, ScratchFolders scratch)
{
    public async ValueTask<Option<string>> RepositoryOfAsync(JobId job, CancellationToken cancellationToken)
    {
        var jobs = await catalog.ListAsync(cancellationToken);
        var shown = jobs.FirstOrDefault(found => found.Job == job);

        if (shown is null || !Started(shown) || scratch.Holds(shown.Repository))
        {
            return Option<string>.None;
        }

        var first = jobs.Where(found => Started(found) && found.Repository == shown.Repository).MinBy(found => found.Submitted);

        return first?.Job == job ? shown.Repository : Option<string>.None;
    }

    private static bool Started(JobSummary job) => job.Parent.IsNone && !job.Companion;
}

internal sealed class RepositoryReadiness(FirstJobs first, IForgeReadiness forges, IBaseFiles files, IRepositoryChecks checks)
{
    public async ValueTask<Option<ReadinessFacts>> OfJobAsync(JobId job, CancellationToken cancellationToken) =>
        await (await first.RepositoryOfAsync(job, cancellationToken)).Match(
            async repository => Option<ReadinessFacts>.Some(await ReadAsync(repository, cancellationToken)),
            () => Task.FromResult(Option<ReadinessFacts>.None));

    public async Task<ReadinessFacts> ReadAsync(string repository, CancellationToken cancellationToken)
    {
        var forge = await forges.OfAsync(repository, cancellationToken);
        var rules = await files.ReadCurrentAsync(repository, RuleFiles.Jobs, cancellationToken);
        var declared = await checks.OfRepositoryAsync(repository, cancellationToken);

        return new ReadinessFacts(
            repository,
            forge,
            rules.Match(file => file.Origin.EditedInWorktree, _ => false),
            declared.File == ChecksFileStatus.Absent && !declared.Origin.Match(origin => origin.EditedInWorktree, () => false));
    }
}

internal sealed class ReadinessActions(SourceControl source, IDocumentation documentation, IReadinessLinks links)
{
    public ValueTask<Result<ForgeCatalog, ForgeError>> ConnectAsync(AccountStatus account, Option<ForgeInfo> forge, CancellationToken cancellationToken) =>
        source.ConnectAsync(SourceControl.Added(account, forge.Bind(found => found.DefaultUrl)), cancellationToken);

    public Task<Result<DocsCommit, DocumentationError>> CommitRulesAsync(string repository, CancellationToken cancellationToken) =>
        documentation.CommitRulesAsync(repository, cancellationToken);

    public Task DeclareChecksAsync(string repository) => links.DeclareChecksAsync(repository);
}

internal sealed class AgentPullRequests(JobBoard board, IMentionedPullRequests mentions)
{
    public async ValueTask<Option<MentionedPullRequest>> OfAsync(Option<JobId> job, CancellationToken cancellationToken)
    {
        var said = job.Bind(board.Find).Match(
            found => found.PullRequest.IsSome ? string.Empty : string.Join('\n', found.Transcript.Entries.Select(Said).Where(text => text.Contains("://", StringComparison.Ordinal))),
            () => string.Empty);

        return said.Length == 0
            ? Option<MentionedPullRequest>.None
            : await job.Match(found => mentions.UnfollowedAsync(found, said, cancellationToken).AsTask(), () => Task.FromResult(Option<MentionedPullRequest>.None));
    }

    private static string Said(ITimelineEntry entry) => entry switch
    {
        MessageEntry message => message.Text,
        ToolEntry tool => tool.Output,
        _ => string.Empty,
    };
}
