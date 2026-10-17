using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avala.Documentation.Contracts;
using Avala.Documentation.Writing;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Documentation.RepositoryFiles;

internal sealed class DocsRulesReader(IBaseFiles files, IWorkingFiles working) : IDocsRules
{
    public const string JobFile = ".avala/jobs.json";

    public const int MaximumBytes = 16 * 1024;

    public const string Section = "docs";

    public const int SectionDepth = 1;

    private static readonly HashSet<string> Fields = ["enabled", "folder"];

    private static readonly JsonSerializerOptions Written = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<Result<DocsRules, DocumentationError>> OfWorktreeAsync(string worktree, CancellationToken cancellationToken) =>
        (await files.ReadAsync(worktree, JobFile, cancellationToken)).Match(
            file => file.Content.Match(Parse, () => DocsRules.Off),
            _ => DocumentationError.Unreadable);

    public async Task<Result<DocsRules, DocumentationError>> OfRepositoryAsync(string repository, CancellationToken cancellationToken) =>
        (await files.ReadCurrentAsync(repository, JobFile, cancellationToken)).Match(
            file => file.Content.Match(Parse, () => DocsRules.Off),
            _ => DocumentationError.Unreadable);

    public async Task<Result<DocsRules, DocumentationError>> SwitchAsync(string repository, bool enabled, CancellationToken cancellationToken)
    {
        if (!(await working.ReadAsync(repository, JobFile, cancellationToken)).TryGetValue(out var current, out _))
        {
            return DocumentationError.Unreadable;
        }

        var switched = current.Match(text => Parse(text).Map(_ => text), () => Result<string, DocumentationError>.Success("{}")).Bind(text => Switched(text, enabled));

        if (!switched.TryGetValue(out var written, out var error))
        {
            return error;
        }

        return (await working.WriteAsync(repository, JobFile, written, cancellationToken)).Match(_ => Parse(written), _ => DocumentationError.Unwritable);
    }

    public static Result<string, DocumentationError> Switched(string text, bool enabled)
    {
        if (JsonNode.Parse(text) is not JsonObject root)
        {
            return DocumentationError.Malformed;
        }

        if (root[Section] is not JsonObject section)
        {
            section = [];
            root[Section] = section;
        }

        section["enabled"] = enabled;

        return root.ToJsonString(Written).ReplaceLineEndings("\n") + "\n";
    }

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
