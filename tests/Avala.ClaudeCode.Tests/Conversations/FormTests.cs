using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Testing;

namespace Avala.ClaudeCode.Tests.Conversations;

public sealed class FormTests
{
    private const string Questions = """
        { "questions": [
            { "question": "Which database?", "header": "Database", "multiSelect": false,
              "options": [ { "label": "PostgreSQL (Recommended)", "description": "Relational" }, { "label": "SQLite", "description": "A file" } ] },
            { "question": "Which extras?", "header": "Extras", "multiSelect": true,
              "options": [ { "label": "Cache", "description": "Redis" }, { "label": "Search", "description": "Elastic" } ] } ] }
        """;

    private static readonly ItemId Item = new("q");

    [Fact]
    public void AQuestionOfClaudeIsAFormWithOneFieldPerQuestion()
    {
        var talk = Asked();

        Assert.Equal(
            new AgentForm(
                FormPurpose.Question,
                "2 questions",
                string.Empty,
                [
                    new FormField("q1", "Database", "Which database?", FieldKind.SingleChoice,
                        [new FormOption("PostgreSQL (Recommended)", "Relational", Recommended: true), new FormOption("SQLite", "A file")], AcceptsFreeText: true),
                    new FormField("q2", "Extras", "Which extras?", FieldKind.MultipleChoice,
                        [new FormOption("Cache", "Redis"), new FormOption("Search", "Elastic")], AcceptsFreeText: true),
                ]),
            Assert.Single(talk.Events.OfType<FormRequested>()).Form,
            new FormComparer());
        Assert.Empty(talk.Events.OfType<ItemStarted>());
    }

    [Fact]
    public void TheAnswerReachesClaudeKeyedByQuestionAndClosesTheFormWhenClaudeReturns()
    {
        var talk = Asked();
        var answer = new FormAnswer(Item, [new FieldAnswer("q1") { Chosen = ["SQLite"] }, new FieldAnswer("q2") { Chosen = ["Cache"], Text = "and logs" }]);

        talk.Take(talk.Conversation.Answer(answer));
        var decision = talk.Decision("r1");
        talk.Receive(Cli.ToolResult("q", "The user answered."));

        Assert.Equal("allow", (string?)decision["behavior"]);
        Assert.Equal("SQLite", (string?)decision["updatedInput"]!["answers"]!["Which database?"]);
        Assert.Equal("Cache, and logs", (string?)decision["updatedInput"]!["answers"]!["Which extras?"]);
        Assert.Equal<IAgentEvent>(
            [new FormAnswered(talk.Session, talk.Turn, Item, answer), new ItemCompleted(talk.Session, talk.Turn, Item, ItemOutcome.Succeeded)],
            talk.Events.SkipWhile(agentEvent => agentEvent is not FormAnswered));
    }

    [Fact]
    public void ADeclinedFormIsRefusedWithItsMessageAndCancelled()
    {
        var talk = Asked();

        talk.Take(talk.Conversation.Answer(new FormAnswer(Item, []) { Declined = true, Message = "Pick yourself." }));
        var decision = talk.Decision("r1");
        talk.Receive(Cli.ToolResult("q", "Pick yourself.", isError: true));

        Assert.Equal("deny", (string?)decision["behavior"]);
        Assert.Equal("Pick yourself.", (string?)decision["message"]);
        Assert.Equal(new ItemCompleted(talk.Session, talk.Turn, Item, ItemOutcome.Cancelled), talk.Events[^1]);
    }

    [Fact]
    public void AnAnswerToAFormThatIsNotOpenIsRefused()
    {
        var talk = Asked();

        Assert.Equal(
            AgentError.NoPendingForm,
            talk.Conversation.Answer(new FormAnswer(new ItemId("other"), [])).Match(_ => default, error => error));
        Assert.Equal(
            AgentError.NoPendingPermission,
            talk.Conversation.Respond(new PermissionDecision(Item, PermissionAnswer.Allow)).Match(_ => default, error => error));
    }

    [Theory]
    [InlineData(true, "allow")]
    [InlineData(false, "deny")]
    public void APlanToApproveIsAConfirmationThatAllowsOrRefusesLeavingPlanMode(bool confirmed, string behavior)
    {
        var plan = """{ "plan": "1. Write\n2. Test" }""";
        var talk = new Talk().Begin().Receive(Cli.ToolUse("p", "ExitPlanMode", plan), Cli.Prompt("r1", "ExitPlanMode", plan, "p"));
        var form = Assert.Single(talk.Events.OfType<FormRequested>()).Form;

        talk.Take(talk.Conversation.Answer(new FormAnswer(new ItemId("p"), [new FieldAnswer("approve") { Confirmed = confirmed, Text = "Keep it small" }])));

        Assert.Equal((FormPurpose.PlanApproval, "1. Write\n2. Test", FieldKind.Confirmation), (form.Purpose, form.Context, form.Fields[0].Kind));
        Assert.Equal(behavior, (string?)talk.Decision("r1")["behavior"]);
    }

    [Fact]
    public void APlanWrittenToClaudesPlansFolderGoesUnaskedAndIsWhatItsApprovalFormShows()
    {
        var written = Cli.OnHost("""{ "file_path": "/home/ana/.claude-work/plans/greeting.md", "content": "Write GREETING.md\n" }""");
        var elsewhere = Cli.OnHost("""{ "file_path": "/home/ana/.claude-work/plans/../notes.md", "content": "Notes" }""");
        var talk = new Talk().Begin().Receive(
            Cli.ToolUse("w1", "Write", written),
            Cli.Hook("h1", "Write", written),
            Cli.Prompt("r1", "Write", written, "w1"),
            Cli.Hook("h2", "Write", elsewhere),
            Cli.ToolUse("e1", "ExitPlanMode", "{}"),
            Cli.Prompt("r2", "ExitPlanMode", "{}", "e1"));

        Assert.Empty(talk.HookAnswer("h1").AsObject());
        Assert.Equal("ask", (string?)talk.HookAnswer("h2")["hookSpecificOutput"]!["permissionDecision"]);
        Assert.Equal("allow", (string?)talk.Decision("r1")["behavior"]);
        Assert.Empty(talk.Events.OfType<PermissionRequested>());
        Assert.Equal("Write the plan", talk.Events.OfType<ItemStarted>().Single().Title);
        Assert.Equal("Write GREETING.md\n", Assert.Single(talk.Events.OfType<FormRequested>()).Form.Context);
    }

    [Fact]
    public async Task InARealPlanModeSessionThePlanClaudeWroteToItsPlansFolderIsTheContextOfItsApprovalAsync()
    {
        var transcript = Path.Combine(Repository.Root.FullName, "tests", "transcripts", "claude-code", "real-plan-approval");
        var home = HostPaths.Rooted("/home/ana");
        var talk = new Talk(plans: Path.Combine(home, ".claude-work", "plans")).Begin();

        foreach (var line in await File.ReadAllLinesAsync(Directory.GetFiles(transcript, "*.jsonl").Single(), TestContext.Current.CancellationToken))
        {
            var onHost = line
                .Replace("${workingDirectory}", Encoded(Talk.WorkingDirectory), StringComparison.Ordinal)
                .Replace("[redacted]/.claude-work/plans/", Encoded(Path.Combine(home, ".claude-work", "plans") + Path.DirectorySeparatorChar), StringComparison.Ordinal);

            if (JsonNode.Parse(onHost)?["out"] is { } output)
            {
                talk.Receive(output.DeepClone());
            }
        }

        Assert.Equal("Write GREETING.md containing # Hello\n", Assert.Single(talk.Events.OfType<FormRequested>()).Form.Context);
        Assert.Contains(talk.Events, agentEvent => agentEvent is ItemStarted { Kind: ItemKind.Other, Title: "Write the plan" });
        Assert.DoesNotContain(talk.Events, agentEvent => agentEvent is PermissionRequested { Title: "Write the plan" });
    }

    private static string Encoded(string text) => System.Text.Json.JsonSerializer.Serialize(text)[1..^1];

    private static Talk Asked() =>
        new Talk().Begin().Receive(Cli.ToolUse("q", "AskUserQuestion", Questions), Cli.Prompt("r1", "AskUserQuestion", Questions, "q"));

    private sealed class FormComparer : IEqualityComparer<AgentForm>
    {
        public bool Equals(AgentForm? x, AgentForm? y) =>
            x is not null && y is not null
            && (x.Purpose, x.Title, x.Context) == (y.Purpose, y.Title, y.Context)
            && x.Fields.Count == y.Fields.Count
            && x.Fields.Zip(y.Fields).All(pair => pair.First with { Options = [] } == pair.Second with { Options = [] }
                && pair.First.Options.SequenceEqual(pair.Second.Options));

        public int GetHashCode(AgentForm obj) => obj.Fields.Count;
    }
}
