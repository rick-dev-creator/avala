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
                var decision = await ReadAsync(writer.Worktree, DocumentationDraft.Decision, cancellationToken);
                var explained = await decision.Match(found => DecideAsync(subject, pending.Number, path, text, found, cancellationToken), () => Task.FromResult(text));
                var written = await WriteAsync(subject.Place.Worktree, path, explained, cancellationToken)
                    && await WriteAsync(subject.Place.Worktree, subject.Folder.Plan(pending.Number), FeatureFolder.PlanDocument(pending.Number, plan), cancellationToken)
                    && await IndexAsync(subject, subject.Documented.Linked(new FeatureVersion(pending.Number, pending.Kind, path, default)), cancellationToken);

                return written
                    ? Result<FeatureVersion, VersionFailure>.Success(new FeatureVersion(pending.Number, pending.Kind, path, default) { Writer = writer.Job, Attempt = subject.Place.Attempt })
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
            && await NewsAsync(subject, path, previous, cancellationToken)
            ? new FeatureVersion(number, VersionKind.AsBuilt, path, default) { Attempt = subject.Place.Attempt }
            : VersionFailure.Unwritable;
    }

    private async Task<string> DecideAsync(Subject subject, int number, string path, string text, string decision, CancellationToken cancellationToken)
    {
        var root = SiteLayout.RootOf(subject.Folder.Root);
        var folder = SiteRecords.Decisions(root);
        var record = SiteRecords.Next(await catalog.ListedAsync(subject.Place.Worktree, cancellationToken), root, decision);
        var toVersion = SiteRecords.Up(folder, root) + path[(root.Length + 1)..];
        var toDecision = SiteRecords.Up(subject.Folder.Path, root) + $"architecture/decisions/{record.File}";

        if (!await WriteAsync(subject.Place.Worktree, $"{folder}/{record.File}", SiteRecords.Shaped(decision, subject.Folder.Feature.Title, number, toVersion), cancellationToken))
        {
            return text;
        }

        foreach (var toc in (await ReadAsync(subject.Place.Worktree, $"{folder}/toc.yml", cancellationToken)).Match(found => new[] { found }, () => []))
        {
            await WriteAsync(subject.Place.Worktree, $"{folder}/toc.yml", toc.TrimEnd() + $"\n- name: {record.Name} {record.Title}\n  href: {record.File}\n", cancellationToken);
        }

        return SiteRecords.Explained(text, record, toDecision);
    }

    private async Task<bool> NewsAsync(Subject subject, string path, Option<string> previous, CancellationToken cancellationToken)
    {
        var root = SiteLayout.RootOf(subject.Folder.Root);
        var news = $"{root}/whats-new.md";

        return await (await ReadAsync(subject.Place.Worktree, news, cancellationToken)).Match(
            async existing =>
            {
                var lead = previous.Match(text => VersionReader.Read(text).Lead, () => string.Empty);
                var entry = $"## {DateTimeOffset.UtcNow:yyyy-MM-dd} · {subject.Folder.Feature.Title}\n\n{lead}\n\n[As built]({path[(root.Length + 1)..]})\n";

                return await WriteAsync(subject.Place.Worktree, news, SiteRecords.WhatsNew(existing, entry), cancellationToken);
            },
            () => Task.FromResult(true));
    }

    private async Task<Option<string>> ReadAsync(string worktree, string path, CancellationToken cancellationToken) =>
        (await files.ReadAsync(worktree, path, cancellationToken)).Match(content => content, _ => Option<string>.None);

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
        var asked = requests.Count > 0 || (await ReadAsync(subject.Place.Worktree, $"{subject.Folder.Path}/{ChangeLog.File}", cancellationToken)).IsSome;

        return await WriteAsync(subject.Place.Worktree, subject.Folder.Readme, subject.Folder.Index(versions, asked), cancellationToken)
            && await WriteAsync(subject.Place.Worktree, subject.Folder.Toc, FeatureFolder.TableOfContents(versions, asked), cancellationToken)
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
