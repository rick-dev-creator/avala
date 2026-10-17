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

    Task ChooseAsync(JobId job, FeatureChoice choice, CancellationToken cancellationToken);

    Option<JobDocumentation> OfJob(JobId job);
}

public sealed record FeatureChosen(JobId Job, FeatureChoice Choice) : IIntegrationEvent;

public sealed record ChangeRequested(JobId Job, ChangeRequest Request) : IIntegrationEvent;

public sealed record VersionWriting(JobId Job, JobId Writer, int Number, VersionKind Kind) : IIntegrationEvent;

public sealed record VersionWritten(JobId Job, Feature Feature, FeatureVersion Version) : IIntegrationEvent;

public sealed record VersionNotWritten(JobId Job, VersionKind Kind, VersionFailure Failure) : IIntegrationEvent;

public sealed record DocumentationDraft(string Content)
{
    public const string Path = ".avala/docs/version.md";
}
