using Avala.Agents.Contracts.Sessions;
using Avala.Verification.Contracts;
using static Avala.Simulator.Scenarios.ScenarioCatalog;

namespace Avala.Simulator.Scenarios;

internal static class ChecksScenarios
{
    public const string Proposal = """
        {
          "checks": [
            { "name": "tests", "command": "git", "arguments": ["--version"], "timeoutSeconds": 60 },
            { "name": "lint", "command": "avala-simulated-linter", "arguments": ["--strict"], "timeoutSeconds": 60 }
          ]
        }
        """;

    public static Scenario DiscoverChecks { get; } = new("discover-checks",
    [
        [
            Thought("The repository has a test command ", "and a lint step; I will run both on the code as it is."),
            new RunCommand(new ItemId("list"), "ls", "README.md\ngreeting.txt", AsksPermission: false),
            new RunCommand(new ItemId("tests"), "git --version", "git version 2.47.0", AsksPermission: false),
            new RunCommand(new ItemId("lint"), "avala-simulated-linter --strict", "0 problems", AsksPermission: false),
            new WriteFile(new ItemId("proposal"), ChecksProposal.Path, Proposal),
            Message("Proposed two checks: ", "tests and lint."),
            .. Bill(1_400, 180, 0.0018m, 0.05),
            new Finish(),
        ],
    ]);

    public static Scenario DiscoverChecksOnCue { get; } = new("discover-checks-on-cue",
    [
        [
            Thought("The repository has a test command ", "and a lint step; I will run both on the code as it is."),
            new RunCommand(new ItemId("list"), "ls", "README.md\ngreeting.txt", AsksPermission: false),
            new AwaitMessage(),
            new RunCommand(new ItemId("tests"), "git --version", "git version 2.47.0", AsksPermission: false),
            new RunCommand(new ItemId("lint"), "avala-simulated-linter --strict", "0 problems", AsksPermission: false),
            new WriteFile(new ItemId("proposal"), ChecksProposal.Path, Proposal),
            Message("Proposed two checks: ", "tests and lint."),
            .. Bill(1_400, 180, 0.0018m, 0.05),
            new Finish(),
        ],
    ]);

    public static IReadOnlyList<Scenario> All { get; } = [DiscoverChecks, DiscoverChecksOnCue];
}
