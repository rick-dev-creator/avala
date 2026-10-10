using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Escalating;
using Avala.Sdk;

namespace Avala.Delegation.Tests.Escalating;

public sealed class AnswerChildInputTests
{
    private const string Child = "5f0c6f0e-8a43-4a52-9d4e-3c1b2a7d9e10";

    [Fact]
    public void AnAnswerCarriesEachFieldsChoicesTextAndConfirmationIntoItsFormAnswer()
    {
        var input = Parsed($$"""
            {"child":"{{Child}}","request":"ask","decision":"allow","message":"Done.","fields":[
              {"id":"color","chosen":["red","blue"]},
              {"id":"name","text":"Ada"},
              {"id":"sure","confirmed":true},
              {"id":"unsure","confirmed":false}]}
            """);

        var answer = input.FormAnswer;
        Assert.Equal((new ItemId("ask"), false, Option<string>.Some("Done.")), (answer.Item, answer.Declined, answer.Message));
        Assert.Equal(
            [("color", "red,blue", Option<string>.None, false), ("name", "", Option<string>.Some("Ada"), false), ("sure", "", Option<string>.None, true), ("unsure", "", Option<string>.None, false)],
            answer.Fields.Select(field => (field.Field, string.Join(',', field.Chosen), field.Text, field.Confirmed)));
    }

    [Fact]
    public void ADeniedAnswerDeclinesTheFormAndDropsItsFields()
    {
        var input = Parsed($$"""{"child":"{{Child}}","request":"ask","decision":"deny","fields":[{"id":"name","text":"Ada"}]}""");

        var answer = input.FormAnswer;
        Assert.True(answer.Declined);
        Assert.Empty(answer.Fields);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"child":"{{child}}","request":"ask","decision":"allow","extra":1}""")]
    [InlineData("""{"child":"{{child}}","request":"ask","decision":"allow","message":3}""")]
    [InlineData("""{"child":"not-a-job","request":"ask","decision":"allow"}""")]
    [InlineData("""{"child":"{{child}}","request":"","decision":"allow"}""")]
    [InlineData("""{"child":"{{child}}","request":"ask","decision":"maybe"}""")]
    [InlineData("""{"child":"{{child}}","request":"ask","decision":"allow","fields":{}}""")]
    [InlineData("""{"child":"{{child}}","request":"ask","decision":"allow","fields":["name"]}""")]
    [InlineData("""{"child":"{{child}}","request":"ask","decision":"allow","fields":[{"id":"name","extra":1}]}""")]
    [InlineData("""{"child":"{{child}}","request":"ask","decision":"allow","fields":[{"text":"Ada"}]}""")]
    [InlineData("""{"child":"{{child}}","request":"ask","decision":"allow","fields":[{"id":""}]}""")]
    [InlineData("""{"child":"{{child}}","request":"ask","decision":"allow","fields":[{"id":"color","chosen":"red"}]}""")]
    [InlineData("""{"child":"{{child}}","request":"ask","decision":"allow","fields":[{"id":"color","chosen":[1]}]}""")]
    [InlineData("""{"child":"{{child}}","request":"ask","decision":"allow","fields":[{"id":"name","text":1}]}""")]
    [InlineData("""{"child":"{{child}}","request":"ask","decision":"allow","fields":[{"id":"sure","confirmed":"yes"}]}""")]
    [InlineData("""{"child":"{{child}}","request":"ask","decision":"allow","fields":[{"id":"name","text":"Ada"},{"id":""}]}""")]
    [InlineData("""{"child":"{{child}}","request":"ask","request":"again","decision":"allow"}""")]
    [InlineData("""{"child":""")]
    public void AnAnswerThatBreaksTheToolsSchemaIsMalformed(string input) =>
        Assert.True(AnswerChildInput.Parse(input.Replace("{{child}}", Child, StringComparison.Ordinal)).IsNone);

    private static AnswerChildInput Parsed(string input) =>
        AnswerChildInput.Parse(input).Match(found => found, () => throw new InvalidOperationException($"The answer was not read: {input}"));
}
