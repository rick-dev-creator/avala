using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

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
    Unwritable,
}

public sealed record Feature(string Slug, string Title);

public sealed record FeatureSuggestion(Feature Feature, bool IsNew);

public sealed record RepositoryFeature(Feature Feature, int Versions);

public sealed record RepositoryDocs(bool Enabled, string Folder, IReadOnlyList<RepositoryFeature> Features)
{
    public static RepositoryDocs Off { get; } = new(false, DocsRules.DefaultFolder, []);
}

public sealed record FeatureChoice(Option<Feature> Feature)
{
    public static FeatureChoice Undocumented { get; } = new(Option<Feature>.None);

    public static FeatureChoice Of(Feature feature) => new(Option<Feature>.Some(feature));
}

public enum VersionKind
{
    Proposed,
    Deviation,
    AsBuilt,
}

public sealed record FeatureVersion(int Number, VersionKind Kind, string Path, DateTimeOffset At)
{
    public Option<JobId> Writer { get; init; }
}

public enum RequestKind
{
    Steer,
    SendBack,
    Message,
}

public sealed record ChangeRequest(int Number, RequestKind Kind, string Text, DateTimeOffset At, int Attempt)
{
    public Option<int> Version { get; init; }
}

public enum VersionFailure
{
    WriterRefused,
    WriterStopped,
    NoDraft,
    Unwritable,
}

public sealed record JobDocumentation(JobId Job, Feature Feature)
{
    public IReadOnlyList<FeatureVersion> Versions { get; init; } = [];

    public Option<JobId> Writing { get; init; }

    public IReadOnlyList<ChangeRequest> Requests { get; init; } = [];
}

public interface IDocumentation
{
    Task<RepositoryDocs> OfRepositoryAsync(string repository, CancellationToken cancellationToken);

    FeatureSuggestion Suggest(string instruction, IReadOnlyList<Feature> features);

    Task<Result<DocsRules, DocumentationError>> SwitchAsync(string repository, bool enabled, CancellationToken cancellationToken);

    Task<Option<FeatureStory>> StoryAsync(string folder, Feature feature, CancellationToken cancellationToken);

    Task<Option<JobDocs>> JobDocsAsync(JobId job, CancellationToken cancellationToken);

    Task ChooseAsync(JobId job, FeatureChoice choice, CancellationToken cancellationToken);

    Option<JobDocumentation> OfJob(JobId job);
}

public sealed record JobDocs(JobDocumentation Documentation, string Repository, Option<FeatureStory> Story, RepositoryDocs Docs);

public sealed record FeatureChosen(JobId Job, FeatureChoice Choice) : IIntegrationEvent;

public sealed record ChangeRequested(JobId Job, ChangeRequest Request) : IIntegrationEvent;

public sealed record VersionWriting(JobId Job, JobId Writer, int Number, VersionKind Kind) : IIntegrationEvent;

public sealed record VersionWritten(JobId Job, Feature Feature, FeatureVersion Version) : IIntegrationEvent;

public sealed record VersionNotWritten(JobId Job, VersionKind Kind, VersionFailure Failure) : IIntegrationEvent;

public sealed record DocumentationDraft(string Content)
{
    public const string Path = ".avala/docs/version.md";
}

public enum C4Level
{
    Context,
    Container,
    Component,
    Dynamic,
}

public enum C4Shape
{
    Person,
    System,
    Container,
    Component,
    Database,
    Queue,
}

public sealed record C4Element(string Alias, string Label, C4Shape Shape, bool External)
{
    public string Technology { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public Option<string> Boundary { get; init; }
}

public sealed record C4Boundary(string Alias, string Label)
{
    public Option<string> Parent { get; init; }
}

public sealed record C4Relation(string From, string To, string Label)
{
    public string Technology { get; init; } = string.Empty;

    public Option<int> Index { get; init; }
}

public sealed record C4Diagram(C4Level Level, string Title, IReadOnlyList<C4Element> Elements, IReadOnlyList<C4Boundary> Boundaries, IReadOnlyList<C4Relation> Relations);

public sealed record DocSection(string Anchor, string Title, string Text);

public sealed record VersionDocument(string Title, string Lead, IReadOnlyList<C4Diagram> Diagrams, IReadOnlyList<DocSection> Sections)
{
    public IReadOnlyList<string> Unread { get; init; } = [];
}

public enum StepKind
{
    Base,
    Added,
    Changed,
    Removed,
    Flow,
}

public sealed record PlayStep(StepKind Kind, string Anchor, string Title, string Text)
{
    public IReadOnlyList<string> Elements { get; init; } = [];

    public IReadOnlyList<C4Relation> Relations { get; init; } = [];
}

public sealed record PlayedVersion(int Number, VersionKind Kind, string File, VersionDocument Document, C4Diagram Structure, IReadOnlyList<PlayStep> Steps)
{
    public IReadOnlyList<C4Element> Removed { get; init; } = [];
}

public sealed record FeatureStory(Feature Feature, IReadOnlyList<PlayedVersion> Versions);
