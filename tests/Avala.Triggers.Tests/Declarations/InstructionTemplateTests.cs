using System.Text.Json;
using Avala.Sdk;
using Avala.Testing;
using Avala.Triggers.Contracts;
using Avala.Triggers.Declarations;

namespace Avala.Triggers.Tests.Declarations;

public sealed class InstructionTemplateTests
{
    private const string Payload = """{ "issue": { "title": "Totals round wrong", "number": 42, "labels": ["bug", "jpy"], "closed": false, "author": { "login": "ana" } }, "note": null }""";

    [Fact]
    public void PlaceholdersRenderPayloadPathsIndexesTheWholePayloadAndTheTrigger()
    {
        using var payload = JsonDocument.Parse(Payload);
        var template = Outcomes.Present(InstructionTemplate.Parse(
            "{{ payload.issue.title }} #{{payload.issue.number}} [{{payload.issue.labels.1}}] {{payload.issue.closed}} {{payload.note}} {{payload.issue.author}} {{payload.missing.path}}|{{trigger.id}} {{trigger.repository}} {{run.at}}"));

        var rendered = template.Render(new TemplateValues(
            new TriggerId(TriggerId.Machine, "issue-opened"),
            "/work/ledger-api",
            new DateTimeOffset(2026, 10, 5, 8, 15, 0, TimeSpan.FromHours(2)),
            payload.RootElement));

        Assert.Equal("""Totals round wrong #42 [jpy] false null { "login": "ana" } |issue-opened /work/ledger-api 2026-10-05 08:15""", rendered);
    }

    [Fact]
    public void WithoutAPayloadItsPlaceholdersRenderNothingAndTheWholePayloadToo()
    {
        var template = Outcomes.Present(InstructionTemplate.Parse("Sweep{{payload}}{{payload.a}} now"));

        Assert.Equal("Sweep now", template.Render(new TemplateValues(new TriggerId(TriggerId.Machine, "sweep"), "/r", DateTimeOffset.UnixEpoch, Option<JsonElement>.None)));
    }

    [Fact]
    public void EachInsertedValueIsCutAtItsLimit()
    {
        using var payload = JsonDocument.Parse($$"""{ "body": "{{new string('x', InstructionTemplate.LongestValue + 10)}}" }""");

        var rendered = Outcomes.Present(InstructionTemplate.Parse("{{payload.body}}"))
            .Render(new TemplateValues(new TriggerId(TriggerId.Machine, "a"), "/r", DateTimeOffset.UnixEpoch, payload.RootElement));

        Assert.Equal(InstructionTemplate.LongestValue, rendered.Length);
    }

    [Theory]
    [InlineData("Fix {{issue.title}}")]
    [InlineData("Fix {{payload.}}")]
    [InlineData("Fix {{payload.a b}}")]
    [InlineData("Fix {{payload.title")]
    [InlineData("Fix {{}}")]
    public void AnUnknownPlaceholderOrAnUnclosedBraceIsAnInvalidTemplate(string text) =>
        Assert.Equal(Option<InstructionTemplate>.None, InstructionTemplate.Parse(text));
}
