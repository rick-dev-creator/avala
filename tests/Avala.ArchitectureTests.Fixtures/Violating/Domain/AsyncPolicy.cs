namespace Avala.Fixtures.Violating.Domain;

public sealed class AsyncPolicy
{
    public Task<int> LimitAsync() => Task.FromResult(10);
}
