using Avala.Fixtures.Violating.Domain;

namespace Avala.Fixtures.Violating.Application;

public sealed class NullableLookup
{
    public string? Name { get; init; }

    public LedgerId? Find(string key) => key.Length > 0 ? new LedgerId(Guid.Empty) : null;

    public Task<string?> DescribeAsync(string key) => Task.FromResult<string?>(key);
}
