using Avala.Documentation.Catalog;
using Avala.Documentation.Contracts;
using Avala.Documentation.Features;
using Avala.Documentation.Records;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Documentation.Writing;

internal sealed class VersionKeeper(IWorkingFiles files, FeatureCatalog catalog, IVerifications verifications, IWorkspaceChanges changes)
{
    public async Task<Result<FeatureVersion, VersionFailure>> KeepAsync(JobPlace writer, Subject subject, PendingWriter pending, CancellationToken cancellationToken)
    {
        var draft = (await files.ReadAsync(writer.Worktree, DocumentationDraft.Path, cancellationToken)).Match(content => content, _ => Option<string>.None);

        return await draft.Match(
            async text =>
            {
                var path = subject.Folder.Version(pending.Number, pending.Kind);
                var plan = pending.Plan.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var written = await WriteAsync(subject.Place.Worktree, path, text, cancellationToken)
                    && await WriteAsync(subject.Place.Worktree, subject.Folder.Plan(pending.Number), FeatureFolder.PlanDocument(pending.Number, plan), cancellationToken)
                    && await IndexAsync(subject, subject.Documented.Linked(new FeatureVersion(pending.Number, pending.Kind, path, default)), cancellationToken);

                return written
                    ? Result<FeatureVersion, VersionFailure>.Success(new FeatureVersion(pending.Number, pending.Kind, path, default) { Writer = writer.Job })
                    : VersionFailure.Unwritable;
            },
            () => Task.FromResult(Result<FeatureVersion, VersionFailure>.Failure(VersionFailure.NoDraft)));
    }

    public async Task<Result<FeatureVersion, VersionFailure>> AsBuiltAsync(Subject subject, CancellationToken cancellationToken)
    {
        var versions = await catalog.VersionsAsync(subject.Place.Worktree, subject.Folder, cancellationToken);
        var number = WriterLauncher.NextNumber(versions);
        var last = versions.Count > 0 ? Option<VersionFile>.Some(versions[^1]) : Option<VersionFile>.None;
        var previous = await last.Match(
            async found => (await files.ReadAsync(subject.Place.Worktree, $"{subject.Folder.Path}/{found.File}", cancellationToken)).Match(content => content, _ => Option<string>.None),
            () => Task.FromResult(Option<string>.None));
        var evidence = new BuildEvidence(subject.Place.Attempt, Checks(subject), await ChangedAsync(subject, cancellationToken));
        var path = subject.Folder.Version(number, VersionKind.AsBuilt);
        var document = AsBuiltDocument.Compose(subject.Folder.Feature, last.Match(found => found.Number, () => number), previous, evidence);

        return await WriteAsync(subject.Place.Worktree, path, document, cancellationToken) && await IndexAsync(subject, subject.Documented.Requests, cancellationToken)
            ? new FeatureVersion(number, VersionKind.AsBuilt, path, default)
            : VersionFailure.Unwritable;
    }

    private IReadOnlyList<CheckOutcome> Checks(Subject subject) =>
        verifications.OfJob(subject.Place.Job) is [.., var report]
            ? [.. report.Checks.Select(check => new CheckOutcome(check.Name, check.Status.ToString()))]
            : [];

    private async Task<IReadOnlyList<ChangedFile>> ChangedAsync(Subject subject, CancellationToken cancellationToken) =>
        (await changes.DiffAsync(subject.Place.Workspace, cancellationToken)).Match(
            diff => (IReadOnlyList<ChangedFile>)[.. diff.Files
                .Where(file => !file.Path.StartsWith(subject.Folder.Root + "/", StringComparison.Ordinal) && !file.Path.StartsWith(".avala/docs/", StringComparison.Ordinal))
                .Select(file => new ChangedFile(file.Path, file.Added, file.Removed))],
            _ => []);

    private async Task<bool> IndexAsync(Subject subject, IReadOnlyList<ChangeRequest> requests, CancellationToken cancellationToken)
    {
        var versions = await catalog.VersionsAsync(subject.Place.Worktree, subject.Folder, cancellationToken);

        return await WriteAsync(subject.Place.Worktree, subject.Folder.Readme, subject.Folder.Index(versions), cancellationToken)
            && await WriteAsync(subject.Place.Worktree, subject.Folder.Toc, FeatureFolder.TableOfContents(versions), cancellationToken)
            && (requests.Count == 0 || await ChangesAsync(subject, requests, versions, cancellationToken));
    }

    private async Task<bool> ChangesAsync(Subject subject, IReadOnlyList<ChangeRequest> requests, IReadOnlyList<VersionFile> versions, CancellationToken cancellationToken)
    {
        var path = $"{subject.Folder.Path}/{ChangeLog.File}";
        var existing = (await files.ReadAsync(subject.Place.Worktree, path, cancellationToken)).Match(content => content.Match(text => text, () => string.Empty), _ => string.Empty);

        return await WriteAsync(subject.Place.Worktree, path, ChangeLog.Merge(existing, subject.Place.Job, subject.Place.Instruction, requests, versions), cancellationToken);
    }

    private async Task<bool> WriteAsync(string worktree, string path, string content, CancellationToken cancellationToken) =>
        (await files.WriteAsync(worktree, path, content, cancellationToken)).Match(_ => true, _ => false);
}
