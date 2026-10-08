using Avala.Sdk;

namespace Avala.Jobs.Jobs;

internal sealed record RepositoryPath
{
    private RepositoryPath(string value) => Value = value;

    public string Value { get; }

    public static Result<RepositoryPath, JobError> Create(string value) =>
        string.IsNullOrWhiteSpace(value) ? JobError.EmptyRepository : new RepositoryPath(value.Trim());
}
