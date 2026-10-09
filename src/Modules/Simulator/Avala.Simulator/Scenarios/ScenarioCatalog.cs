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
            new Ask(new ItemId("question"), new AgentForm(
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
                ])),
            Message("The service stores its orders ", "in the chosen database."),
            .. Bill(2_200, 140, 0.0090m, 0.27),
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
            new Draw(new ItemId("flow"), "Job flow", "text/vnd.mermaid",
            [
                "flowchart LR\n",
                "  Submitted --> Running\n",
                "  Running --> Checking --> AwaitingReview\n",
            ]),
            Message("The diagram shows ", "the host and its plugins."),
            .. Bill(5_200, 900, 0.0420m, 0.40),
            new Finish(),
        ],
    ]);

    public static IReadOnlyList<Scenario> All { get; } =
        [Reply, Edit, FixAfterFeedback, RewriteChecks, Permission, RepeatedPermission, OutsideEdit, Question, PlanApproval, Crash, LeftOpen, Hang, Canvas];

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

    private static Say Thought(params string[] chunks) => new(new ItemId("thinking"), ItemKind.Reasoning, "Thinking", chunks);

    private static Say Message(params string[] chunks) => new(new ItemId("reply"), ItemKind.Message, "Reply", chunks);

    private static UpdatePlan Plan(PlanStepStatus change, PlanStepStatus tests) =>
        new([new PlanStep("Write the change", change), new PlanStep("Run the tests", tests)]);

    private static IStep[] Bill(long input, long output, decimal cost, double used) =>
    [
        new ReportUsage(new TokenUsage(input, output, input / 2, input / 10, output / 4), new Cost(cost, "USD")),
        new ReportLimit(new UsageLimit("5h", used, Option<DateTimeOffset>.None)),
    ];
}
