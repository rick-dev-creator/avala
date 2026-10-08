namespace Avala.Fixtures.Violating.Domain;

public sealed class ThrowingPolicy
{
    public int Limit(int requested) => requested > 0 ? requested : throw new ArgumentOutOfRangeException(nameof(requested));
}
