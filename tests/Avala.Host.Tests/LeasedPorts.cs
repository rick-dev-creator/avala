namespace Avala.Host.Tests;

internal static class LeasedPorts
{
    private const int First = 25_000;

    private const int Last = 32_499;

    private const int Block = 100;

    public static string Settings() =>
        $$""" "ports": { "first": {{First}}, "last": {{Last}} } """;

    public static string OneBlock() =>
        $$""" "ports": { "first": {{Last + 1}}, "last": {{Last + Block}}, "perWorktree": {{Block}} } """;
}
