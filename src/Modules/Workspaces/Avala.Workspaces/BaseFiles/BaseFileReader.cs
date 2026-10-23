using System.Collections.Immutable;
using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Provisioning;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.BaseFiles;

internal sealed class BaseFileReader(IGit git, IWorkspaceStore store) : IBaseFiles
{
    private const string Head = "HEAD";

    private const int Kept = 1024;

    private ImmutableDictionary<(string Repository, string Commit, string Path), Option<string>> blobs = ImmutableDictionary<(string, string, string), Option<string>>.Empty;

    private ImmutableDictionary<(string Repository, string Blob), string> contents = ImmutableDictionary<(string, string), string>.Empty;

    public async ValueTask<Result<BaseFile, WorkspaceFailure>> ReadAsync(string worktree, string path, CancellationToken cancellationToken) =>
        await (await store.FindAtAsync(worktree, cancellationToken))
            .ToResult(WorkspaceFailure.UnknownWorkspace)
            .BindAsync(workspace => ReadAsync(workspace.Location, workspace.Rules, path, cancellationToken));

    public async ValueTask<Result<BaseFile, WorkspaceFailure>> ReadCurrentAsync(string repository, string path, CancellationToken cancellationToken)
    {
        if (!(await git.FindRepositoryRootAsync(repository, cancellationToken)).TryGetValue(out var root, out var failure)
            || !(await git.ResolveCommitAsync(root, Head, cancellationToken)).TryGetValue(out var commit, out failure)
            || !WorkspaceLocation.Create(root, root).TryGetValue(out var checkout, out _))
        {
            return failure;
        }

        return await ReadAsync(checkout, commit, path, cancellationToken);
    }

    private async Task<Result<BaseFile, WorkspaceFailure>> ReadAsync(
        WorkspaceLocation location,
        CommitSha commit,
        string path,
        CancellationToken cancellationToken)
    {
        if (!(await CommittedBlobAsync(location.Repository, commit, path, cancellationToken)).TryGetValue(out var committed, out var failure)
            || !(await ContentAsync(location.Repository, committed, cancellationToken)).TryGetValue(out var content, out failure)
            || !(await git.WorktreeBlobAsync(location, path, cancellationToken)).TryGetValue(out var current, out failure))
        {
            return failure;
        }

        return new BaseFile(path, new FileOrigin(commit.Value, current != committed), content);
    }

    private async Task<Result<Option<string>, WorkspaceFailure>> ContentAsync(
        string repository,
        Option<string> blob,
        CancellationToken cancellationToken) =>
        await blob.Match(
            found => BlobContentAsync(repository, found, cancellationToken).MapAsync(Option<string>.Some),
            () => Task.FromResult(Result<Option<string>, WorkspaceFailure>.Success(Option<string>.None)));

    private async Task<Result<Option<string>, WorkspaceFailure>> CommittedBlobAsync(
        string repository,
        CommitSha commit,
        string path,
        CancellationToken cancellationToken) =>
        Volatile.Read(ref blobs).TryGetValue((repository, commit.Value, path), out var kept)
            ? kept
            : (await git.CommittedBlobAsync(repository, commit, path, cancellationToken)).Map(found => Keep(ref blobs, (repository, commit.Value, path), found));

    private async Task<Result<string, WorkspaceFailure>> BlobContentAsync(string repository, string blob, CancellationToken cancellationToken) =>
        Volatile.Read(ref contents).TryGetValue((repository, blob), out var kept)
            ? kept
            : (await git.BlobContentAsync(repository, blob, cancellationToken)).Map(found => Keep(ref contents, (repository, blob), found));

    private static TValue Keep<TKey, TValue>(ref ImmutableDictionary<TKey, TValue> cache, TKey key, TValue value)
        where TKey : notnull
    {
        ImmutableInterlocked.Update(ref cache, current => (current.Count >= Kept ? current.Clear() : current).SetItem(key, value));

        return value;
    }
}
