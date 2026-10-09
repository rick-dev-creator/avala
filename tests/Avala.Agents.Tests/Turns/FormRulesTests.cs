using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Agents.Turns;

namespace Avala.Agents.Tests.Turns;

public sealed class FormRulesTests
{
    private static readonly ItemId Item = new("question");

    private static readonly AgentForm Form = new(
        FormPurpose.Question,
        "Set up storage",
        "The service needs storage.",
        [
            new FormField("one", "One", "Pick one", FieldKind.SingleChoice, [new FormOption("a", ""), new FormOption("b", "")], AcceptsFreeText: true),
            new FormField("many", "Many", "Pick any", FieldKind.MultipleChoice, [new FormOption("a", ""), new FormOption("b", "")]),
            new FormField("text", "Text", "Write", FieldKind.FreeText, []),
            new FormField("confirm", "Confirm", "Go on?", FieldKind.Confirmation, []),
        ]);

    public static TheoryData<string, FieldAnswer> FittingAnswers => new()
    {
        { "one option", new FieldAnswer("one") { Chosen = ["a"] } },
        { "free text instead of an option", new FieldAnswer("one") { Text = "c" } },
        { "several options", new FieldAnswer("many") { Chosen = ["a", "b"] } },
        { "text", new FieldAnswer("text") { Text = "orders" } },
        { "a refusal", new FieldAnswer("confirm") { Confirmed = false } },
    };

    public static TheoryData<string, FieldAnswer> UnfittingAnswers => new()
    {
        { "two options in a single choice", new FieldAnswer("one") { Chosen = ["a", "b"] } },
        { "an option and text in a single choice", new FieldAnswer("one") { Chosen = ["a"], Text = "c" } },
        { "nothing in a single choice", new FieldAnswer("one") },
        { "an unknown option", new FieldAnswer("one") { Chosen = ["z"] } },
        { "text where none is accepted", new FieldAnswer("many") { Text = "c" } },
        { "the same option twice", new FieldAnswer("many") { Chosen = ["a", "a"] } },
        { "blank text", new FieldAnswer("text") { Text = " " } },
        { "an option for a confirmation", new FieldAnswer("confirm") { Chosen = ["a"] } },
    };

    [Theory]
    [MemberData(nameof(FittingAnswers))]
    public void AcceptsAnAnswerThatFitsItsField(string fit, FieldAnswer answer) =>
        Assert.True(Form.Accepts(Complete(answer)), fit);

    [Theory]
    [MemberData(nameof(UnfittingAnswers))]
    public void RejectsAnAnswerThatDoesNotFitItsField(string misfit, FieldAnswer answer) =>
        Assert.False(Form.Accepts(Complete(answer)), misfit);

    [Fact]
    public void RejectsAnswersThatMissAFieldAddAnUnknownOneOrRepeatOne()
    {
        var complete = Complete(new FieldAnswer("text") { Text = "orders" });

        Assert.False(Form.Accepts(complete with { Fields = complete.Fields.Skip(1).ToList() }));
        Assert.False(Form.Accepts(complete with { Fields = [.. complete.Fields, new FieldAnswer("unknown") { Text = "x" }] }));
        Assert.False(Form.Accepts(complete with { Fields = [.. complete.Fields.Skip(1), complete.Fields[1]] }));
    }

    [Fact]
    public void ADeclinedFormCarriesNoFieldAnswers()
    {
        Assert.True(Form.Accepts(new FormAnswer(Item, []) { Declined = true, Message = "Not now." }));
        Assert.False(Form.Accepts(Complete(new FieldAnswer("text") { Text = "orders" }) with { Declined = true }));
    }

    private static FormAnswer Complete(FieldAnswer answer) =>
        new(Item, [.. Defaults().Select(given => given.Field == answer.Field ? answer : given)]);

    private static FieldAnswer[] Defaults() =>
    [
        new("one") { Chosen = ["a"] },
        new("many") { Chosen = ["b"] },
        new("text") { Text = "orders" },
        new("confirm") { Confirmed = true },
    ];
}
