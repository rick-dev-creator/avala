namespace Avala.ArchitectureTests.Rules;

internal static class Violations
{
    public static IReadOnlyList<string> Named(IEnumerable<string> violations) =>
        [.. violations.Select(name => name[(name.LastIndexOf('.') + 1)..]).Order(StringComparer.Ordinal)];
}
