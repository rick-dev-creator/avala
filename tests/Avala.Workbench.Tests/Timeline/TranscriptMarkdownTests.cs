using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Timeline;

public sealed class TranscriptMarkdownTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private readonly SessionId session = SessionId.New();
    private readonly TurnId turn = TurnId.New();

    [Fact]
    public void PromptsAndRepliesAreWrittenInOrderUnderTheJobsTitle()
    {
        var transcript = Transcript.Empty.WithPrompts("Fix the failing test", [])
            .Apply(new TurnStarted(session, turn), Now)
            .Apply(new MessageQueued(session, turn, "Keep the old alias"), Now);
        transcript = Said(transcript, "reply", "Fixed, and the alias stays.");

        Assert.Equal(
            "# Fix flaky test\n\n**You**\n\nFix the failing test\n\n**You**\n\nKeep the old alias\n\nFixed, and the alias stays.\n",
            TranscriptMarkdown.Write("Fix flaky test", transcript, MarkdownChoices.Collapsed));
    }

    [Fact]
    public void AToolIsOneLineAndItsOutputIsFencedOnlyWhenExpanded()
    {
        var transcript = Ran("test", "Run go test ./...", "ok money", ItemOutcome.Succeeded);
        var key = transcript.Entries.OfType<ToolEntry>().Single().Key;

        Assert.Equal("# Job\n\n- Run go test ./...\n", TranscriptMarkdown.Write("Job", transcript, MarkdownChoices.Collapsed));
        Assert.Equal("# Job\n\n- Run go test ./...\n\n```\nok money\n```\n", TranscriptMarkdown.Write("Job", transcript, new MarkdownChoices(new HashSet<string> { key })));
    }

    [Theory]
    [InlineData(ItemOutcome.Failed, "- Run make (failed)")]
    [InlineData(ItemOutcome.Cancelled, "- Run make (stopped)")]
    public void AToolThatDidNotSucceedSaysSoOnItsLine(ItemOutcome outcome, string line) =>
        Assert.Contains(line, TranscriptMarkdown.Write("Job", Ran("make", "Run make", string.Empty, outcome), MarkdownChoices.Collapsed), StringComparison.Ordinal);

    [Fact]
    public void AFenceIsLongerThanAnyRunOfBackticksInTheOutput()
    {
        var transcript = Ran("cat", "Run cat README.md", "```go\nfunc A()\n```", ItemOutcome.Succeeded);
        var key = transcript.Entries.OfType<ToolEntry>().Single().Key;

        Assert.EndsWith("````\n```go\nfunc A()\n```\n````\n", TranscriptMarkdown.Write("Job", transcript, new MarkdownChoices(new HashSet<string> { key })), StringComparison.Ordinal);
    }

    [Fact]
    public void AShownImageIsWrittenAsAnImageOfItsKeptCopyNamedByItsCaptionOrElseItsFile()
    {
        var transcript = Transcript.Empty.Apply(new TurnStarted(session, turn), Now)
            .Apply(new MediaShown(session, turn, new ItemId("shown"), new Attachment("screen.png", "image/png", "/data/a b/screen.png"), "The checkout page"))
            .Apply(new MediaShown(session, turn, new ItemId("bare"), new Attachment("icon.png", "image/png", "/data/icon.png"), string.Empty));

        Assert.Equal(
            "# Job\n\n![The checkout page](</data/a b/screen.png>)\n\n![icon.png](</data/icon.png>)\n",
            TranscriptMarkdown.Write("Job", transcript, MarkdownChoices.Collapsed));
    }

    [Fact]
    public void ReasoningIsLeftOutUnlessItIsChosen()
    {
        var transcript = Transcript.Empty.Apply(new TurnStarted(session, turn), Now)
            .Apply(new ItemStarted(session, turn, new ItemId("think"), ItemKind.Reasoning, "Thinking"), Now)
            .Apply(new ItemProgressed(session, turn, new ItemId("think"), "Check the exponent.\n\nThen the tax."), Now);

        Assert.Equal("# Job\n", TranscriptMarkdown.Write("Job", transcript, MarkdownChoices.Collapsed));
        Assert.Equal("# Job\n\n> Check the exponent.\n>\n> Then the tax.\n", TranscriptMarkdown.Write("Job", transcript, MarkdownChoices.Collapsed with { Reasoning = true }));
    }

    [Fact]
    public void ACanvasIsWrittenAsItsTitleAndItsSourceInTheLanguageOfItsMediaType()
    {
        var transcript = Transcript.Empty.Apply(new TurnStarted(session, turn), Now)
            .Apply(new CanvasStarted(session, turn, new ItemId("chart"), "Orbits", "text/vnd.mermaid"), Now)
            .Apply(new CanvasSnapshot(new CanvasId(turn, new ItemId("chart")), session, "Orbits", "text/vnd.mermaid", "graph TD; Sun-->Earth", CanvasStatus.Completed, true));

        Assert.Equal("# Job\n\n- Orbits\n\n```mermaid\ngraph TD; Sun-->Earth\n```\n", TranscriptMarkdown.Write("Job", transcript, MarkdownChoices.Collapsed));
    }

    [Theory]
    [InlineData("Fix JPY rounding in invoice totals", "fix-jpy-rounding-in-invoice-totals.md")]
    [InlineData("  Build sistema-solar.html: the sun!  ", "build-sistema-solar-html-the-sun.md")]
    [InlineData("¿?", "conversation.md")]
    public void AnExportIsNamedAfterTheJobsTitle(string title, string name) =>
        Assert.Equal(name, TranscriptExport.FileName(title));

    private Transcript Said(Transcript transcript, string item, string text) =>
        transcript.Apply(new ItemStarted(session, turn, new ItemId(item), ItemKind.Message, "Reply"), Now)
            .Apply(new ItemProgressed(session, turn, new ItemId(item), text), Now)
            .Apply(new ItemCompleted(session, turn, new ItemId(item), ItemOutcome.Succeeded), Now);

    private Transcript Ran(string item, string title, string output, ItemOutcome outcome) =>
        Transcript.Empty.Apply(new TurnStarted(session, turn), Now)
            .Apply(new ItemStarted(session, turn, new ItemId(item), ItemKind.Command, title), Now)
            .Apply(new ItemProgressed(session, turn, new ItemId(item), output), Now)
            .Apply(new ItemCompleted(session, turn, new ItemId(item), outcome), Now);
}
