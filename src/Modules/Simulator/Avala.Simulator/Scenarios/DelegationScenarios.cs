using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using static Avala.Simulator.Scenarios.ScenarioCatalog;

namespace Avala.Simulator.Scenarios;

internal static class DelegationScenarios
{
    public static Scenario Delegated { get; } = new("delegate",
    [
        [
            Thought("Two independent pieces of work. ", "I will hand each to a sub-agent."),
            new CallTools(
            [
                Delegation("delegate-notes", """{ "instruction": "[simulate: notes] Write the release notes" }"""),
                Delegation("delegate-todo", """{ "instruction": "[simulate: todo] Write the to-do list" }"""),
            ]),
            Message("Both pieces of work came back ", "with their evidence."),
            .. Bill(2_100, 160, 0.0080m, 0.22),
            new Finish(),
        ],
    ]);

    public static Scenario DelegatedConflict { get; } = new("delegate-conflict",
    [
        [
            Thought("Two takes on the notes. ", "I will ask two sub-agents."),
            new CallTools(
            [
                Delegation("delegate-notes", """{ "instruction": "[simulate: notes] Write the release notes" }"""),
                Delegation("delegate-revised", """{ "instruction": "[simulate: notes-revised] Revise the release notes" }"""),
            ]),
            Message("The sub-agents reported back."),
            .. Bill(2_000, 150, 0.0075m, 0.22),
            new Finish(),
        ],
    ]);

    public static Scenario DelegatedWaiting { get; } = new("delegate-waiting",
    [
        [
            Thought("The migration needs a person's approval. ", "A sub-agent will ask for it."),
            Delegation("delegate-migrate", """{ "instruction": "[simulate: permission] Migrate the database" }"""),
            Message("The migration came back."),
            .. Bill(1_700, 120, 0.0065m, 0.22),
            new Finish(),
        ],
        Resumed("The migration came back after the restart."),
    ]);

    public static Scenario DelegatedPaused { get; } = new("delegate-paused",
    [
        [
            Thought("The notes can be written ", "by a sub-agent."),
            Delegation("delegate-notes", """{ "instruction": "[simulate: notes-paused] Write the release notes" }"""),
            Message("The notes came back."),
            .. Bill(1_700, 120, 0.0065m, 0.22),
            new Finish(),
        ],
        Resumed("The notes came back after the restart."),
    ]);

    public static Scenario DelegatedAcross { get; } = new("delegate-across",
    [
        [
            Thought("Two pieces of work, ", "one sub-agent after the other."),
            Delegation("delegate-notes", """{ "instruction": "[simulate: notes] Write the release notes" }"""),
            Delegation("delegate-todo", """{ "instruction": "[simulate: todo] Write the to-do list" }"""),
            Message("Both pieces of work came back ", "with their evidence."),
            .. Bill(2_100, 160, 0.0080m, 0.22),
            new Finish(),
        ],
    ]);

    public static Scenario PausedNotes { get; } = new("notes-paused",
    [
        [
            Thought("Gathering what changed ", "for the release notes."),
            .. Bill(900, 40, 0.0030m, 0.18),
        ],
        [
            Thought("Back at the notes ", "after the restart."),
            new WriteFile(new ItemId("notes"), "NOTES.md", "# Notes\n\nWritten by a delegated simulator after a restart.\n"),
            Message("Wrote NOTES.md."),
            .. Bill(1_400, 90, 0.0050m, 0.20),
            new Finish(),
        ],
    ]);

    public static Scenario DelegatedLoosely { get; } = new("delegate-loosen",
    [
        [
            Thought("The notes could be written unattended."),
            Delegation("delegate-autonomous", """{ "instruction": "[simulate: notes] Write the release notes", "autonomy": "autonomous" }"""),
            Delegation("delegate-notes", """{ "instruction": "[simulate: notes] Write the release notes" }"""),
            Message("The notes came back."),
            .. Bill(1_900, 140, 0.0070m, 0.22),
            new Finish(),
        ],
    ]);

    public static Scenario DelegatedOnModels { get; } = new("delegate-model",
    [
        [
            Thought("The notes need a careful model."),
            Delegation("delegate-huge", """{ "instruction": "[simulate: notes] Write the release notes", "model": "huge" }"""),
            Delegation("delegate-small", """{ "instruction": "[simulate: notes] Write the release notes", "model": "simulated-small", "effort": "low" }"""),
            Message("The notes came back."),
            .. Bill(1_900, 140, 0.0070m, 0.22),
            new Finish(),
        ],
    ]);

    public static Scenario DelegatedExpensively { get; } = new("delegate-expensive",
    [
        [
            Thought("The index needs rebuilding. ", "A sub-agent will do it."),
            Delegation("delegate-index", """{ "instruction": "[simulate: expensive] Rebuild the search index" }"""),
            Message("The sub-agent reported back."),
            .. Bill(1_800, 130, 0.0100m, 0.22),
            new Finish(),
        ],
    ]);

    public static Scenario Recursive { get; } = new("recursive",
    [
        [
            Thought("This is better done by a sub-agent."),
            Delegation("delegate-again", """{ "instruction": "[simulate: recursive] Delegate the work again" }"""),
            Message("The delegation came back."),
            .. Bill(1_600, 110, 0.0060m, 0.22),
            new Finish(),
        ],
    ]);

    public static Scenario Notes { get; } = new("notes",
    [
        [
            Thought("Writing the release notes."),
            new WriteFile(new ItemId("notes"), "NOTES.md", "# Notes\n\nWritten by a delegated simulator.\n"),
            Message("Wrote NOTES.md."),
            .. Bill(1_400, 90, 0.0050m, 0.20),
            new Finish(),
        ],
    ]);

    public static Scenario RevisedNotes { get; } = new("notes-revised",
    [
        [
            Thought("Revising the release notes."),
            new WriteFile(new ItemId("notes"), "NOTES.md", "# Notes\n\nRevised by another delegated simulator.\n"),
            Message("Revised NOTES.md."),
            .. Bill(1_450, 95, 0.0052m, 0.20),
            new Finish(),
        ],
    ]);

    public static Scenario Todo { get; } = new("todo",
    [
        [
            Thought("Writing the to-do list."),
            new WriteFile(new ItemId("todo"), "TODO.md", "# To do\n\n- Ship the release.\n"),
            Message("Wrote TODO.md."),
            .. Bill(1_300, 85, 0.0048m, 0.20),
            new Finish(),
        ],
    ]);

    public static Scenario Expensive { get; } = new("expensive",
    [
        [
            Thought("Rebuilding the whole index ", "takes a lot of tokens."),
            new WriteFile(new ItemId("index"), "INDEX.md", "# Index\n\nHalf rebuilt.\n"),
            new ReportUsage(new TokenUsage(90_000, 7_000, 45_000, 9_000, 1_750), new Cost(0.6000m, "USD")),
            new ReportLimit(new UsageLimit("5h", 0.30, Option<DateTimeOffset>.None)),
        ],
        [
            Message("Stopped where the budget ran out."),
            .. Bill(1_000, 60, 0.0030m, 0.30),
            new Finish(),
        ],
    ]);

    public static Scenario DelegatedSteered { get; } = new("delegate-steered",
    [
        [
            Thought("The migration needs a person's approval. ", "A sub-agent will ask for it."),
            Delegation("delegate-migrate", """{ "instruction": "[simulate: permission] Migrate the database" }"""),
            Message("The migration came back."),
            .. Bill(1_700, 120, 0.0065m, 0.22),
            new Finish(),
        ],
        [
            Thought("Carrying on ", "while the sub-agent works."),
            new AwaitMessage(),
            Message("Folded the sub-agent's report into this turn."),
            .. Bill(1_200, 90, 0.0050m, 0.24),
            new Finish(),
        ],
    ]);

    public static IReadOnlyList<Scenario> All { get; } =
    [
        Delegated, DelegatedConflict, DelegatedWaiting, DelegatedLoosely, DelegatedOnModels, DelegatedExpensively, Recursive, DelegatedPaused, DelegatedAcross, DelegatedSteered,
        Notes, RevisedNotes, Todo, Expensive, PausedNotes,
    ];

    private static CallTool Delegation(string item, string input) => new(new ItemId(item), ScenarioCatalog.Delegate, input);

    private static IStep[] Resumed(string message) =>
    [
        new Recall(new ItemId("told")),
        Message(message),
        .. Bill(1_100, 70, 0.0040m, 0.24),
        new Finish(),
    ];
}
