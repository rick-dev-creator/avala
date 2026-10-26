using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using static Avala.Simulator.Scenarios.ScenarioCatalog;

namespace Avala.Simulator.Scenarios;

internal static class ToolScenarios
{
    public static Scenario DeniedTool { get; } = new("denied-tool",
    [
        [
            Thought("The greeting style is settled, ", "so I will file it as an issue."),
            new UseTool(new ItemId("issue"), ItemKind.Mcp, "create_issue", "create_issue", "{\"title\":\"Greetings are a single heading line\"}", "Created issue #7.", AsksPermission: true),
            Message("Filed the greeting style."),
            .. Bill(1_400, 90, 0.0060m, 0.20),
            new Finish(),
        ],
    ]);

    public static IReadOnlyList<Scenario> All { get; } = [DeniedTool];
}
