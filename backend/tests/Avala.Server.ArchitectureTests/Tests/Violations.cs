namespace Avala.Server.ArchitectureTests.Tests;

internal static class Violations
{
    public static IReadOnlyList<string> Sorted(IEnumerable<string> violations) =>
        [.. violations.Distinct().Order(StringComparer.Ordinal)];
}
