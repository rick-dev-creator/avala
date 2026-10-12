using System.Collections.Immutable;

namespace Avala.Sdk.Processes;

public sealed record ProcessRequest(string FileName, IReadOnlyList<string> Arguments, Option<string> WorkingDirectory = default)
{
    public IReadOnlyDictionary<string, string> Environment { get; init; } = ImmutableDictionary<string, string>.Empty;
}
