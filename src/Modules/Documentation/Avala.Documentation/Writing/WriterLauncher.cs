using Avala.Documentation.Catalog;
using Avala.Documentation.Contracts;
using Avala.Documentation.Features;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Documentation.Writing;

internal sealed record Launched(JobId Writer, int Number);

internal sealed class WriterLauncher(IJobs jobs, IRoleSettings roles, FeatureCatalog catalog, IWorkingFiles files)
{
    public async Task<Result<Launched, JobRejection>> StartAsync(Subject subject, IReadOnlyList<string> plan, VersionKind kind, CancellationToken cancellationToken)
    {
        var versions = await catalog.VersionsAsync(subject.Place.Worktree, subject.Folder, cancellationToken);
        var number = NextNumber(versions);
        var previous = await (versions.Count > 0 ? Option<VersionFile>.Some(versions[^1]) : Option<VersionFile>.None).Match(
            async last => (await files.ReadAsync(subject.Place.Worktree, $"{subject.Folder.Path}/{last.File}", cancellationToken)).Match(content => content, _ => Option<string>.None),
            () => Task.FromResult(Option<string>.None));
        var brief = new WriterBrief(subject.Folder.Feature, number, kind, subject.Place.Instruction, plan, previous)
        {
            Asked = [.. subject.Documented.Requests.Where(request => request.Version.IsNone).Select(request => request.Text)],
        };
        var request = new JobRequest(subject.Place.Repository, brief.Text())
        {
            Parent = subject.Place.Job,
            Autonomy = Autonomy.Autonomous,
            Companion = true,
            Model = await roles.OfRoleAsync(subject.Place.Worktree, ChildRole.Research, cancellationToken),
        };

        return (await jobs.SubmitAsync(request, cancellationToken)).Map(writer => new Launched(writer, number));
    }

    public async Task DiscardAsync(JobId writer, CancellationToken cancellationToken) => await jobs.DiscardAsync(writer, cancellationToken);

    public static int NextNumber(IReadOnlyList<VersionFile> versions) => versions.Count == 0 ? 1 : versions.Max(version => version.Number) + 1;
}
