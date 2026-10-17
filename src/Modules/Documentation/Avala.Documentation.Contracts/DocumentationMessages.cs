namespace Avala.Documentation.Contracts;

public sealed record DocsRules(bool Enabled, string Folder)
{
    public const string DefaultFolder = "docs/features";

    public static DocsRules Off { get; } = new(false, DefaultFolder);
}

public enum DocumentationError
{
    Unreadable,
    TooLarge,
    Malformed,
    UnknownField,
    InvalidFolder,
}

public sealed record Feature(string Slug, string Title);

public sealed record FeatureSuggestion(Feature Feature, bool IsNew);

public static class DocumentationDraft
{
    public const string Path = ".avala/docs/version.md";
}
