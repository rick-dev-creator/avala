namespace Avala.Host.Tests;

internal static class LeasedPorts
{
    private const int First = 25_000;

    private const int PerTest = 100;

    private const int Tests = 4;

    private const int Runs = 18;

    public static string Settings(int test) =>
        $$""" "ports": { "first": {{FirstOf(test)}}, "last": {{FirstOf(test) + PerTest - 1}} } """;

    private static int FirstOf(int test) => First + (Environment.ProcessId % Runs * Tests + test) * PerTest;
}
