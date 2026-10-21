using static Avala.Simulator.Scenarios.ScenarioCatalog;

namespace Avala.Simulator.Scenarios;

internal static class RetryScenarios
{
    public static Scenario Retried { get; } = new("retry",
    [
        [
            new Retry(1, 10, TimeSpan.FromSeconds(1), "overloaded"),
            new Retry(2, 10, TimeSpan.FromSeconds(2), "overloaded"),
            Message("Back on track: ", "the greeting is in place."),
            .. Bill(1_200, 80, 0.0042m, 0.12),
            new Finish(),
        ],
    ]);

    public static IReadOnlyList<Scenario> All { get; } = [Retried];
}
