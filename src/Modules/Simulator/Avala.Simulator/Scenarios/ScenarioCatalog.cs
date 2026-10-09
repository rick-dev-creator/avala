using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Simulator.Scenarios;

internal static class ScenarioCatalog
{
    private const string Tag = "[simulate:";

    private static readonly IStep[] FixedCalculator =
    [
        Thought("The feedback says the sum is wrong. ", "I will fix it."),
        new WriteFile(new ItemId("fix"), "calculator.txt", "add(2, 2) = 4\n"),
        new RunCommand(new ItemId("test"), "dotnet test", "Passed: 12, Failed: 0", AsksPermission: false),
        Message("Fixed the sum, ", "the tests pass now."),
        .. Bill(3_900, 310, 0.0190m, 0.24),
        new Finish(),
    ];

    public static Scenario Reply { get; } = new("reply",
    [
        [
            Thought("The user greets me. ", "No change is needed."),
            Message("Hello! ", "This is a simulated ", "Claude Code turn."),
            .. Bill(1_200, 80, 0.0042m, 0.12),
            new Finish(),
        ],
    ]);

    public static Scenario Edit { get; } = new("edit",
    [
        [
            Thought("I will add a greeting ", "and run the tests."),
            Plan(PlanStepStatus.InProgress, PlanStepStatus.Pending),
            new WriteFile(new ItemId("edit"), "GREETING.md", "# Hello\n\nWritten by the simulator.\n"),
            Plan(PlanStepStatus.Done, PlanStepStatus.InProgress),
            new RunCommand(new ItemId("test"), "dotnet test", "Passed: 12, Failed: 0", AsksPermission: false),
            Plan(PlanStepStatus.Done, PlanStepStatus.Done),
            Message("Added GREETING.md ", "and the tests pass."),
            .. Bill(4_800, 620, 0.0310m, 0.18),
            new Finish(),
        ],
    ]);

    public static Scenario FixAfterFeedback { get; } = new("fix-after-feedback",
    [
        [
            Thought("A quick change ", "to the calculator."),
            new WriteFile(new ItemId("edit"), "calculator.txt", "add(2, 2) = 5 BROKEN\n"),
            Message("The calculator is ready."),
            .. Bill(3_100, 240, 0.0150m, 0.21),
            new Finish(),
        ],
        FixedCalculator,
    ]);

    public static Scenario RewriteChecks { get; } = new("rewrite-checks",
    [
        [
            Thought("The checks are in the way. ", "I will empty them."),
            new WriteFile(new ItemId("checks"), ".avala/checks.json", "{ \"checks\": [] }\n"),
            new WriteFile(new ItemId("edit"), "calculator.txt", "add(2, 2) = 5 BROKEN\n"),
            Message("The calculator is ready ", "and no check stands in the way."),
            .. Bill(3_300, 260, 0.0160m, 0.22),
            new Finish(),
        ],
        FixedCalculator,
    ]);

    public static Scenario Tools { get; } = new("tools",
    [
        [
            Thought("I will look around, ", "then write the greeting and the notes."),
            Plan(PlanStepStatus.InProgress, PlanStepStatus.Pending),
            new UseTool(new ItemId("search"), ItemKind.Search, "Search greeting", "greeting", "greeting in src/**/*.cs", "src/Greeter.cs:3: // greeting goes here", AsksPermission: false),
            new UseTool(new ItemId("fetch"), ItemKind.Web, "Fetch https://example.com/style", "https://example.com/style", "https://example.com/style\nHow should a greeting be written?", "Greetings start with a heading.", AsksPermission: true),
            new UseTool(new ItemId("load"), ItemKind.Other, "Load the canvas tool", "canvas", "canvas", "canvas", AsksPermission: false),
            new UseTool(
                new ItemId("explore"),
                ItemKind.Subagent,
                "Subagent: Find the greeting style",
                "Find the greeting style",
                "Read the repository and say how greetings are written.",
                "Reading the repository.\n\nGreetings are a single heading line.",
                AsksPermission: false),
            new WriteFile(new ItemId("edit"), "GREETING.md", "# Hello\n"),
            new WriteThroughCommand(new ItemId("notes"), "cat > NOTES.md <<'EOF'\n# Notes\nWritten through the shell.\nEOF", "NOTES.md", "# Notes\nWritten through the shell.\n"),
            Plan(PlanStepStatus.Done, PlanStepStatus.Done),
            Message("Wrote GREETING.md ", "and NOTES.md."),
            .. Bill(3_600, 410, 0.0210m, 0.19),
            new Finish(),
        ],
    ]);

    public static Scenario Permission { get; } = new("permission",
    [
        [
            Thought("The schema changed, ", "so the database needs a migration."),
            .. Bill(2_400, 150, 0.0110m, 0.30),
            new RunCommand(new ItemId("migrate"), "dotnet ef database update", "Applied 2 migrations.", AsksPermission: true),
            Message("The database is up to date."),
            new Finish(),
        ],
    ]);

    public static Scenario WaitingPermission { get; } = new("waiting-permission",
    [
        [
            Thought("The schema changed, ", "so the database needs a migration."),
            .. Bill(2_400, 150, 0.0110m, 0.30),
            new RunCommand(new ItemId("migrate"), "dotnet ef database update", "Applied 2 migrations.", AsksPermission: true),
            Message("The database is up to date."),
            new Finish(),
        ],
        [
            Thought("The harness restarted ", "while I waited for permission to migrate."),
            Message("I left the migration for you to run."),
            .. Bill(1_200, 80, 0.0050m, 0.31),
            new Finish(),
        ],
    ]);

    public static Scenario RepeatedPermission { get; } = new("repeated-permission",
    [
        [
            Thought("Two databases ", "need the same migration."),
            .. Bill(2_600, 170, 0.0120m, 0.31),
            new RunCommand(new ItemId("migrate"), "dotnet ef database update", "Applied 2 migrations.", AsksPermission: true),
            new RunCommand(new ItemId("migrate-again"), "dotnet ef database update", "Applied 2 migrations.", AsksPermission: true),
            Message("Both databases are up to date."),
            new Finish(),
        ],
    ]);

    public static Scenario OutsideEdit { get; } = new("outside-edit",
    [
        [
            Thought("I will build, ", "then note the result next to the repository."),
            new RunCommand(new ItemId("build"), "dotnet build", "Build succeeded.", AsksPermission: true),
            new WriteFile(new ItemId("note"), "../avala-outside-note.txt", "Built.\n"),
            Message("Built and noted."),
            .. Bill(1_700, 90, 0.0060m, 0.26),
            new Finish(),
        ],
    ]);

    public static Scenario Question { get; } = new("question",
    [
        [
            Thought("The service needs storage, ", "and the choice is the team's."),
            DatabaseQuestion,
            Message("The service stores its orders ", "in the chosen database."),
            .. Bill(2_200, 140, 0.0090m, 0.27),
            new Finish(),
        ],
    ]);

    public static Scenario Governed { get; } = new("governed",
    [
        [
            Thought("The service needs storage ", "and its schema a migration."),
            DatabaseQuestion,
            .. Bill(2_500, 160, 0.0100m, 0.28),
            new RunCommand(new ItemId("migrate"), "dotnet ef database update", "Applied 2 migrations.", AsksPermission: true),
            Message("The orders are stored ", "and the database is migrated."),
            new Finish(),
        ],
    ]);

    public static Scenario UnsharedThought { get; } = new("unshared-thought",
    [
        [
            Thought(),
            Message("Done, ", "without sharing how I got there."),
            .. Bill(1_100, 70, 0.0040m, 0.12),
            new Finish(),
        ],
    ]);

    public static Scenario Fields { get; } = new("fields",
    [
        [
            Thought("The release needs three answers ", "before it can be tagged."),
            new Ask(new ItemId("release"), new AgentForm(
                FormPurpose.Other,
                "Prepare the release",
                "The changelog is drafted; the tag and its notes are not.",
                [
                    new FormField("tag", "Tag", "Which tag should the release get?", FieldKind.FreeText, []),
                    new FormField(
                        "targets",
                        "Targets",
                        "Where should it be published?",
                        FieldKind.MultipleChoice,
                        [new FormOption("NuGet", "The package feed.", Recommended: true), new FormOption("GitHub", "A release page.")]),
                    new FormField("notes", "Notes", "Publish the drafted notes as they are?", FieldKind.Confirmation, []),
                ])),
            Message("The release is prepared."),
            .. Bill(1_900, 110, 0.0070m, 0.22),
            new Finish(),
        ],
    ]);

    public static Scenario PlanApproval { get; } = new("plan-approval",
    [
        [
            Thought("Planning before ", "touching anything."),
            Plan(PlanStepStatus.Pending, PlanStepStatus.Pending),
            new Ask(new ItemId("plan"), new AgentForm(
                FormPurpose.PlanApproval,
                "Approve the plan",
                "1. Write the change\n2. Run the tests",
                [new FormField("approve", "Plan", "Proceed with this plan?", FieldKind.Confirmation, [], AcceptsFreeText: true)])),
            new WriteFile(new ItemId("edit"), "PLAN.md", "# Plan\n\nApproved and carried out.\n"),
            Message("The plan is carried out."),
            .. Bill(2_000, 120, 0.0080m, 0.28),
            new Finish(),
        ],
    ]);

    public static Scenario Crash { get; } = new("crash",
    [
        [
            Thought("Starting the work."),
            .. Bill(900, 20, 0.0030m, 0.33),
            new Open(new ItemId("reply"), ItemKind.Message, "Reply"),
            new Crash("The simulated agent process exited unexpectedly."),
        ],
        [
            Thought("The previous run stopped halfway. ", "Picking up from there."),
            Message("Resumed and finished the work."),
            .. Bill(1_100, 70, 0.0040m, 0.34),
            new Finish(),
        ],
    ]);

    public static Scenario LeftOpen { get; } = new("left-open",
    [
        [
            Thought("Building the project."),
            new Open(new ItemId("build"), ItemKind.Command, "dotnet build"),
            Message("The build is running."),
            .. Bill(1_500, 60, 0.0050m, 0.35),
            new Finish(),
        ],
    ]);

    public static Scenario Hang { get; } = new("hang",
    [
        [],
        [
            Thought("Back at work ", "after the pause."),
            Message("Finished what I was doing."),
            .. Bill(1_300, 90, 0.0045m, 0.36),
            new Finish(),
        ],
    ]);

    public static Scenario Canvas { get; } = new("canvas",
    [
        [
            Thought("A diagram explains this best."),
            new Draw(new ItemId("diagram"), "Architecture", "image/svg+xml",
            [
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"240\" height=\"120\">",
                "<rect x=\"10\" y=\"40\" width=\"80\" height=\"40\" rx=\"6\" fill=\"#4f46e5\"/>",
                "<rect x=\"150\" y=\"40\" width=\"80\" height=\"40\" rx=\"6\" fill=\"#0ea5e9\"/>",
                "<line x1=\"90\" y1=\"60\" x2=\"150\" y2=\"60\" stroke=\"#111827\" stroke-width=\"2\"/>",
                "</svg>",
            ]),
            new Draw(new ItemId("flow"), "Job flow", "image/svg+xml",
            [
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 420 60\" font-size=\"12\">",
                "<g fill=\"none\" stroke=\"#6b7280\"><rect x=\"5\" y=\"15\" width=\"100\" height=\"30\" rx=\"6\"/>",
                "<rect x=\"160\" y=\"15\" width=\"100\" height=\"30\" rx=\"6\"/><rect x=\"315\" y=\"15\" width=\"100\" height=\"30\" rx=\"6\"/>",
                "<path d=\"M105 30h55M260 30h55\"/></g>",
                "<g fill=\"#111827\" text-anchor=\"middle\"><text x=\"55\" y=\"34\">Submitted</text><text x=\"210\" y=\"34\">Running</text><text x=\"365\" y=\"34\">Checking</text></g>",
                "</svg>",
            ]),
            new Draw(new ItemId("notes"), "Notes", "text/markdown",
            [
                "# The host and its plugins\n\n",
                "- The host knows no module.\n",
                "- Every module is a **plugin**.\n",
            ]),
            Message("The diagrams show ", "the host and its plugins."),
            .. Bill(5_200, 900, 0.0420m, 0.40),
            new Finish(),
        ],
    ]);

    public static Scenario UnofferedCanvas { get; } = new("unoffered-canvas",
    [
        [
            new Draw(new ItemId("report"), "Job report", "text/html",
            [
                "<h1>Job report</h1>\n",
                "<p>Submitted, then running.</p>\n",
            ]),
            Message("I wrote the job report in HTML."),
            .. Bill(1_200, 80, 0.0040m, 0.30),
            new Finish(),
        ],
    ]);

    public static Scenario MermaidCanvas { get; } = new("mermaid-canvas",
    [
        [
            new Draw(new ItemId("flow"), "Job flow", "text/vnd.mermaid",
            [
                "flowchart LR\n",
                "  Submitted --> Running\n",
                "  Running --> Checking\n",
            ]),
            Message("I drew the job flow in Mermaid."),
            .. Bill(1_200, 80, 0.0040m, 0.30),
            new Finish(),
        ],
    ]);

    public static Scenario Markdown { get; } = new("markdown",
    [
        [
            Message(
                "## Rounding fixed\n\nJPY has **no minor ",
                "units**, so `ToMinor` now reads the exponent:\n\n- JPY: 0 decimals\n",
                "- USD: 2 decimals\n\n```go\nfunc ToMinor(amount Money) int64 {\n",
                "    return amount.Units * pow10(amount.Currency.Exponent)\n}\n```\n\n",
                "| Currency | Exponent |\n| --- | --- |\n| JPY | 0 |\n\n",
                "See [ISO 4217](https://www.iso.org/iso-4217-currency-codes.html) and [the notes](file:///etc/passwd)."),
            .. Bill(1_400, 260, 0.0060m, 0.20),
            new Finish(),
        ],
    ]);

    public static Scenario Processes { get; } = new("processes",
    [
        [
            Thought("I will build the service ", "and start it to try it out."),
            new Spawn(new ItemId("build"), "dotnet build", Workload.Build),
            new Spawn(new ItemId("serve"), "dotnet run", Workload.Server),
            Message("The service is running ", "on the port this worktree was given."),
            .. Bill(2_900, 210, 0.0130m, 0.29),
            new Finish(),
        ],
    ]);

    public static Scenario FollowUp { get; } = new("follow-up",
    [
        [
            Thought("I will start a changelog. ", "The team should hear about it afterwards."),
            new WriteFile(new ItemId("changelog"), "CHANGELOG.md", "# Changelog\n\n- Started by the simulator.\n"),
            new CallTool(
                new ItemId("propose"),
                ProposeFollowUp,
                """{ "instruction": "[simulate: reply] Announce the changelog to the team", "reason": "The team should hear about the changelog." }"""),
            Message("Started the changelog ", "and proposed announcing it."),
            .. Bill(2_500, 180, 0.0120m, 0.25),
            new Finish(),
        ],
    ]);

    public static Scenario NearLimit { get; } = new("near-limit",
    [
        [
            Thought("A long piece of work ", "that uses most of the window."),
            Message("Done, ", "though the usage window is nearly spent."),
            new ReportUsage(new TokenUsage(9_000, 700, 4_500, 900, 175), new Cost(0.0600m, "USD")),
            new ReportLimitResetting("5h", 0.95, TimeSpan.FromSeconds(2)),
            new Finish(),
        ],
    ]);

    public static Scenario SpentWindow { get; } = new("spent-window",
    [
        [
            Thought("A long piece of work ", "that uses most of the window."),
            Message("Still going, ", "though the usage window is nearly spent."),
            new ReportUsage(new TokenUsage(9_000, 700, 4_500, 900, 175), new Cost(0.0600m, "USD")),
            new ReportLimitResetting("5h", 0.95, TimeSpan.FromHours(1)),
        ],
        [
            Message("Picked up where the window ran out."),
            .. Bill(800, 40, 0.0020m, 0.10),
            new Finish(),
        ],
    ]);

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

    public static IReadOnlyList<Scenario> All { get; } =
    [
        Reply, Edit, Tools, FixAfterFeedback, RewriteChecks, Permission, WaitingPermission, RepeatedPermission, OutsideEdit, Question, Governed, UnsharedThought, Fields, PlanApproval,
        Crash, LeftOpen, Hang, Canvas, UnofferedCanvas, MermaidCanvas, Markdown, Processes, FollowUp, NearLimit, SpentWindow, Delegated, DelegatedConflict, DelegatedWaiting, DelegatedLoosely,
        DelegatedExpensively, Recursive, Notes, RevisedNotes, Todo, Expensive,
    ];

    public const string ProposeFollowUp = "propose_follow_up";

    public const string Delegate = "delegate";

    public static string NameIn(string firstMessage) =>
        Tagged(firstMessage, ReplayRequest.TimedTag).Match(
            recording => new ReplayRequest(recording, AsRecorded: true).Scenario,
            () => Tagged(firstMessage, ReplayRequest.CompressedTag).Match(
                recording => new ReplayRequest(recording, AsRecorded: false).Scenario,
                () => Tagged(firstMessage, Tag).Bind(Named).Match(scenario => scenario.Name, () => Reply.Name)));

    public static Option<Scenario> Named(string name) =>
        All.FirstOrDefault(scenario => string.Equals(scenario.Name, name, StringComparison.OrdinalIgnoreCase)).ToOption();

    private static Option<string> Tagged(string message, string tag)
    {
        var start = message.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
        var end = start < 0 ? -1 : message.IndexOf(']', start);

        return end < 0 ? Option<string>.None : message[(start + tag.Length)..end].Trim();
    }

    private static Ask DatabaseQuestion => new(new ItemId("question"), new AgentForm(
        FormPurpose.Question,
        "Choose a database",
        "The service needs to store its orders.",
        [
            new FormField(
                "database",
                "Database",
                "Which database should the service use?",
                FieldKind.SingleChoice,
                [
                    new FormOption("PostgreSQL", "Relational, already run by the team.", Recommended: true),
                    new FormOption("SQLite", "A single file, no server to run."),
                ],
                AcceptsFreeText: true),
        ]));

    private static Say Thought(params string[] chunks) => new(new ItemId("thinking"), ItemKind.Reasoning, "Thinking", chunks);

    private static Say Message(params string[] chunks) => new(new ItemId("reply"), ItemKind.Message, "Reply", chunks);

    private static CallTool Delegation(string item, string input) => new(new ItemId(item), Delegate, input);

    private static UpdatePlan Plan(PlanStepStatus change, PlanStepStatus tests) =>
        new([new PlanStep("Write the change", change), new PlanStep("Run the tests", tests)]);

    private static IStep[] Bill(long input, long output, decimal cost, double used) =>
    [
        new ReportUsage(new TokenUsage(input, output, input / 2, input / 10, output / 4), new Cost(cost, "USD")),
        new ReportLimit(new UsageLimit("5h", used, Option<DateTimeOffset>.None)),
    ];
}
