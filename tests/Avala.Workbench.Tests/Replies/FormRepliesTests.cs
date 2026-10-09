using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Workbench.Replies;

namespace Avala.Workbench.Tests.Replies;

public sealed class FormRepliesTests
{
    private static readonly FormField Database = new(
        "database",
        "Database",
        "Which database?",
        FieldKind.SingleChoice,
        [new FormOption("PostgreSQL", "Relational", Recommended: true), new FormOption("SQLite", "A file")],
        AcceptsFreeText: true);

    private static readonly FormField Notes = new("notes", "Notes", "Anything else?", FieldKind.FreeText, []);

    [Fact]
    public void TheRecommendedChoiceIsReadyToSend()
    {
        var choice = FieldChoice.Recommended(Database);

        var answer = FormReplies.Answer(new ItemId("question"), [choice]);

        Assert.True(choice.IsComplete);
        Assert.Equal(["PostgreSQL"], Assert.Single(answer.Fields).Chosen);
    }

    [Fact]
    public void TextTypedForASingleChoiceIsSentAloneInPlaceOfAnOption()
    {
        var choice = FieldChoice.Recommended(Database) with { Text = "  CockroachDB " };

        var answered = choice.Answer();

        Assert.Equal((0, Option<string>.Some("CockroachDB")), (answered.Chosen.Count, answered.Text));
    }

    [Fact]
    public void AFieldLeftUnansweredHoldsTheFormBack()
    {
        Assert.False(FormReplies.IsComplete([FieldChoice.Recommended(Database), FieldChoice.Recommended(Notes)]));
        Assert.True(FormReplies.IsComplete([FieldChoice.Recommended(Database), FieldChoice.Recommended(Notes) with { Text = "None" }]));
    }

    [Fact]
    public void DecliningCarriesTheNoteForTheAgentOnlyWhenOneWasWritten()
    {
        var withNote = FormReplies.Decline(new ItemId("question"), " Ask the team first ");
        var withoutNote = FormReplies.Decline(new ItemId("question"), " ");

        Assert.Equal((true, Option<string>.Some("Ask the team first"), 0), (withNote.Declined, withNote.Message, withNote.Fields.Count));
        Assert.True(withoutNote.Message.IsNone);
    }
}
