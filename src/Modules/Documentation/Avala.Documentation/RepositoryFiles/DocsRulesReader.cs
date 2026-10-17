using System.Text;
using System.Text.Json;
using Avala.Documentation.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Documentation.RepositoryFiles;

internal sealed class DocsRulesReader(IBaseFiles files)
{
    public const string JobFile = ".avala/jobs.json";

    public const int MaximumBytes = 16 * 1024;

    public const string Section = "docs";

    public const int SectionDepth = 1;

    private static readonly HashSet<string> Fields = ["enabled", "folder"];

    public async Task<Result<DocsRules, DocumentationError>> OfWorktreeAsync(string worktree, CancellationToken cancellationToken) =>
        (await files.ReadAsync(worktree, JobFile, cancellationToken)).Match(
            file => file.Content.Match(Parse, () => DocsRules.Off),
            _ => DocumentationError.Unreadable);

    public static Result<DocsRules, DocumentationError> Parse(string text) =>
        Encoding.UTF8.GetByteCount(text) > MaximumBytes ? DocumentationError.TooLarge
        : JsonSections.Read(text, Section, SectionDepth, DocumentationError.Malformed, section => section.Match(Declared, () => DocsRules.Off));

    private static Result<DocsRules, DocumentationError> Declared(JsonElement section)
    {
        if (section.ValueKind != JsonValueKind.Object)
        {
            return DocumentationError.Malformed;
        }

        if (section.EnumerateObject().Any(field => !Fields.Contains(field.Name)))
        {
            return DocumentationError.UnknownField;
        }

        return Enabled(section).Bind(enabled => Folder(section).Map(folder => new DocsRules(enabled, folder)));
    }

    private static Result<bool, DocumentationError> Enabled(JsonElement section) =>
        !section.TryGetProperty("enabled", out var declared) ? false
        : declared.ValueKind is JsonValueKind.True or JsonValueKind.False ? declared.GetBoolean()
        : DocumentationError.Malformed;

    private static Result<string, DocumentationError> Folder(JsonElement section)
    {
        if (!section.TryGetProperty("folder", out var declared))
        {
            return DocsRules.DefaultFolder;
        }

        if (declared.ValueKind != JsonValueKind.String)
        {
            return DocumentationError.Malformed;
        }

        var folder = (declared.GetString() ?? string.Empty).TrimEnd('/');
        var parts = folder.Split('/');

        return folder.Length == 0 || folder.StartsWith('/') || folder.Contains('\\') || parts.Any(part => part is "" or "." or "..")
            ? DocumentationError.InvalidFolder
            : folder;
    }
}
