using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.RepositoryRules;

internal sealed record RuleFileDraft(string Content, bool Exists);

internal sealed class RuleFileEditing(IWorkingFiles files, IEnumerable<IRuleFileFormat> formats)
{
    private readonly IReadOnlyList<IRuleFileFormat> known = [.. formats];

    public bool CanEdit(string path) => known.Any(format => format.Path == path);

    public async Task<Result<RuleFileDraft, WorkspaceFailure>> ReadAsync(string repository, string path, string template, CancellationToken cancellationToken) =>
        (await files.ReadAsync(repository, path, cancellationToken)).Map(found => found.Match(
            content => new RuleFileDraft(content, Exists: true),
            () => new RuleFileDraft(template, Exists: false)));

    public Option<Enum> Rejection(string path, string content) =>
        known.FirstOrDefault(format => format.Path == path).ToOption().Bind(format => format.Rejection(content));

    public async Task<Result<string, WorkspaceFailure>> WriteAsync(string repository, string path, string content, CancellationToken cancellationToken) =>
        await files.WriteAsync(repository, path, content, cancellationToken);
}
