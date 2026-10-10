using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Triggers.Catalog;
using Avala.Triggers.Contracts;
using Avala.Triggers.Declarations;
using Avala.Triggers.TriggerFiles;

namespace Avala.Triggers.Tests.TriggerFiles;

public sealed class TriggerFileParserTests
{
    private static readonly string Repository = Repositories.Key(Triggered.Repository);

    [Fact]
    public void AMachineFileListsItsRepositoriesAndReadsEachTriggerWithItsDefaults()
    {
        var content = Outcomes.Succeeds(TriggerFileParser.ParseMachine(Declared.Machine(
            Declared.Trigger(""" "schedule": { "everyMinutes": 90 } """),
            $"""["{Triggered.Repository}", "{Triggered.Repository}/"]""")));

        var trigger = Assert.Single(content.Triggers);
        Assert.Equal([Repository], content.Repositories);
        Assert.Equal(new TriggerId(TriggerId.Machine, "nightly"), trigger.Id);
        Assert.Equal(Repository, trigger.Repository);
        Assert.Equal((TriggerKind.Interval, 90), (trigger.Kind, trigger.Schedule.Match(schedule => schedule.EveryMinutes, () => 0)));
        Assert.Equal(
            (true, CatchUp.None, TriggerTarget.Job, Autonomy.Supervised, 3, 1, Option<Agents.Contracts.Connections.ConnectionName>.None),
            (trigger.Enabled, trigger.CatchUp, trigger.Target, trigger.Autonomy, trigger.Attempts, trigger.Concurrency, trigger.Connection));
    }

    [Fact]
    public void AFixedTimeWithWeekdaysAndAWebhookWithItsRateAreRead()
    {
        var content = Outcomes.Succeeds(TriggerFileParser.ParseMachine(Declared.Machine(
            Declared.Trigger(""" "schedule": { "at": "02:30", "days": ["mon", "fri"] }, "catchUp": "once", "target": "loop", "autonomy": "autonomous", "attempts": 5, "concurrency": 2, "connection": "work", "enabled": false """)
            + "," + """{ "id": "issue", "repository": "/work/ledger-api", "instruction": "Triage {{payload.issue.title}}", "webhook": { "secretEnv": "HOOK_SECRET", "ratePerHour": 30 } }""")));

        var (scheduled, hook) = (content.Triggers[0], content.Triggers[1]);
        Assert.Equal(new TimeOnly(2, 30), scheduled.Schedule.Match(schedule => schedule.At, () => default));
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Friday], scheduled.Schedule.Match(schedule => schedule.Days, () => []));
        Assert.Equal((false, CatchUp.Once, TriggerTarget.Loop, Autonomy.Autonomous, 5, 2, "work"), (scheduled.Enabled, scheduled.CatchUp, scheduled.Target, scheduled.Autonomy, scheduled.Attempts, scheduled.Concurrency, scheduled.Connection.Match(name => name.Value, () => string.Empty)));
        Assert.Equal(new WebhookRule("HOOK_SECRET", 30), hook.Webhook.Match(rule => rule, () => new WebhookRule(string.Empty, 0)));
        Assert.Equal(("/hooks/issue", TriggerKind.Webhook), (hook.Hook.Match(path => path, () => string.Empty), hook.Kind));
    }

    [Theory]
    [InlineData("{", TriggerError.Malformed)]
    [InlineData("[]", TriggerError.Malformed)]
    [InlineData("""{ "triggers": [], "triggers": [] }""", TriggerError.Malformed)]
    [InlineData("""{ "repositories": [], "extra": 1 }""", TriggerError.UnknownField)]
    [InlineData("""{ "repositories": [""] }""", TriggerError.Malformed)]
    [InlineData("""{ "triggers": [ { "id": "-bad", "repository": "/r", "instruction": "x", "schedule": { "everyMinutes": 5 } } ] }""", TriggerError.InvalidKey)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "schedule": { "everyMinutes": 5 } }, { "id": "a", "repository": "/r", "instruction": "y", "schedule": { "everyMinutes": 5 } } ] }""", TriggerError.DuplicateKey)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "schedule": { "everyMinutes": 5 } } ] }""", TriggerError.MissingInstruction)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": " ", "schedule": { "everyMinutes": 5 } } ] }""", TriggerError.MissingInstruction)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x" } ] }""", TriggerError.InvalidSchedule)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "schedule": { "everyMinutes": 5 }, "webhook": { "secretEnv": "S" } } ] }""", TriggerError.InvalidSchedule)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "schedule": { "everyMinutes": 5, "at": "02:00" } } ] }""", TriggerError.InvalidSchedule)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "schedule": { "days": ["mon"] } } ] }""", TriggerError.InvalidSchedule)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "schedule": { "everyMinutes": 0 } } ] }""", TriggerError.InvalidInterval)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "schedule": { "everyMinutes": 525601 } } ] }""", TriggerError.InvalidInterval)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "schedule": { "at": "25:00" } } ] }""", TriggerError.InvalidTime)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "schedule": { "at": "2:30" } } ] }""", TriggerError.InvalidTime)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "schedule": { "at": "02:30", "days": [] } } ] }""", TriggerError.InvalidDays)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "schedule": { "at": "02:30", "days": ["mon", "mon"] } } ] }""", TriggerError.InvalidDays)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "schedule": { "at": "02:30", "days": ["monday"] } } ] }""", TriggerError.InvalidDays)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "schedule": { "everyMinutes": 5 }, "catchUp": "all" } ] }""", TriggerError.InvalidCatchUp)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "webhook": { "secretEnv": "S" }, "catchUp": "once" } ] }""", TriggerError.InvalidCatchUp)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "schedule": { "everyMinutes": 5 }, "autonomy": "yolo" } ] }""", TriggerError.InvalidAutonomy)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "schedule": { "everyMinutes": 5 }, "target": "pr" } ] }""", TriggerError.InvalidTarget)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "schedule": { "everyMinutes": 5 }, "attempts": 11 } ] }""", TriggerError.InvalidAttempts)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "schedule": { "everyMinutes": 5 }, "concurrency": 0 } ] }""", TriggerError.InvalidConcurrency)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "webhook": { "secretEnv": "S", "ratePerHour": 3601 } } ] }""", TriggerError.InvalidRate)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "webhook": { "secretEnv": "1BAD" } } ] }""", TriggerError.InvalidSecret)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "webhook": { "secret": "plain" } } ] }""", TriggerError.UnknownField)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "Fix {{issue.title}}", "schedule": { "everyMinutes": 5 } } ] }""", TriggerError.InvalidTemplate)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "Fix {{payload.title", "schedule": { "everyMinutes": 5 } } ] }""", TriggerError.InvalidTemplate)]
    [InlineData("""{ "triggers": [ { "id": "a", "instruction": "x", "schedule": { "everyMinutes": 5 } } ] }""", TriggerError.MissingRepository)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "schedule": { "everyMinutes": 5 }, "when": "now" } ] }""", TriggerError.UnknownField)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "schedule": { "everyMinutes": "5" } } ] }""", TriggerError.Malformed)]
    public void ARejectedMachineFileNamesItsErrorAndLoadsNothing(string text, TriggerError error) =>
        Assert.Equal(error, Outcomes.FailsWith(TriggerFileParser.ParseMachine(text)));

    [Fact]
    public void AFileOverItsSizeIsTooLarge() =>
        Assert.Equal(TriggerError.TooLarge, Outcomes.FailsWith(TriggerFileParser.ParseMachine($$"""{ "repositories": ["{{new string('a', TriggerFileParser.MaximumBytes)}}"] }""")));

    [Fact]
    public void AnInstructionOverItsLengthIsAnInvalidTemplate() =>
        Assert.Equal(
            TriggerError.InvalidTemplate,
            Outcomes.FailsWith(TriggerFileParser.ParseRepository($$"""{ "triggers": [ { "id": "a", "instruction": "{{new string('x', TriggerFields.LongestInstruction + 1)}}", "schedule": { "everyMinutes": 5 } } ] }""", Repository)));

    [Theory]
    [InlineData("""{ "triggers": [ { "id": "a", "instruction": "x", "webhook": { "secretEnv": "S" } } ] }""", TriggerError.WebhookNotAllowed)]
    [InlineData("""{ "triggers": [ { "id": "a", "repository": "/r", "instruction": "x", "schedule": { "everyMinutes": 5 } } ] }""", TriggerError.UnknownField)]
    [InlineData("""{ "repositories": [] }""", TriggerError.UnknownField)]
    public void ARepositoryFileCannotDeclareWebhooksRepositoriesOrOtherRepositoriesTriggers(string text, TriggerError error) =>
        Assert.Equal(error, Outcomes.FailsWith(TriggerFileParser.ParseRepository(text, Repository)));

    [Fact]
    public void ARepositoryTriggerBelongsToItsRepository()
    {
        var trigger = Assert.Single(Outcomes.Succeeds(TriggerFileParser.ParseRepository("""{ "triggers": [ { "id": "sweep", "instruction": "x", "schedule": { "everyMinutes": 5 } } ] }""", Repository)));

        Assert.Equal((new TriggerId(Repository, "sweep"), Repository), (trigger.Id, trigger.Repository));
    }
}
