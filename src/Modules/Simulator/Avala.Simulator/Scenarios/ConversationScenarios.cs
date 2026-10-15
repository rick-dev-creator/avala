using Avala.Agents.Contracts.Sessions;
using static Avala.Simulator.Scenarios.ScenarioCatalog;

namespace Avala.Simulator.Scenarios;

internal static class ConversationScenarios
{
    public static Scenario RemoteControl { get; } = new("remote-control",
    [
        [
            Message("I added the refund endpoint."),
            .. Bill(3_200, 410, 0.0210m, 0.12),
            new Finish(),
        ],
        [
            new FromDevice("Also cover partial refunds"),
            Message("Partial refunds are covered by the tests now."),
            .. Bill(3_900, 520, 0.0260m, 0.14),
            new Finish(),
        ],
    ]);

    public static Scenario Attachments { get; } = new("attachments",
    [
        [
            new RecallAttachments(new ItemId("seen")),
            .. Bill(2_400, 120, 0.0120m, 0.11),
            new Finish(),
        ],
    ]);

    public static Scenario Mentions { get; } = new("mentions",
    [
        [
            Message("The rounding module is in place."),
            .. Bill(2_100, 140, 0.0110m, 0.10),
            new Finish(),
        ],
        [
            new Recall(new ItemId("told")),
            .. Bill(2_300, 90, 0.0100m, 0.11),
            new Finish(),
        ],
    ]);

    public static IReadOnlyList<Scenario> All { get; } = [RemoteControl, Attachments, Mentions];
}
