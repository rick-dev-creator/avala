using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;

namespace Avala.ClaudeCode.Tests.Conversations;

public sealed class DraftTests
{
    [Fact]
    public void AFileWriteIsDraftedOnceItsPathArrivesThenAgainEvery512CharactersUntilItStarts()
    {
        var input = Cli.OnHost($$"""{ "file_path": "/work/site/index.html", "content": "{{new string('x', 1400)}}" }""");
        var named = input.IndexOf("\"content\"", StringComparison.Ordinal);
        string[] partials = [input[..10], input[10..named], .. input[named..].Chunk(200).Select(chunk => new string(chunk))];
        var talk = new Talk().Begin().Receive(Cli.DraftedTool("m1", 1, "t1", "Write", partials)).Receive(Cli.Stopped(1)).Receive(Cli.ToolUse("t1", "Write", input));

        var drafts = talk.Events.OfType<ItemDrafted>().ToList();

        Assert.All(drafts, drafted => Assert.Equal((new ItemId("t1"), ItemKind.FileEdit, "Write site/index.html"), (drafted.Item, drafted.Kind, drafted.Title)));
        Assert.Equal(named, drafts[0].Received);
        Assert.Equal(Enumerable.Range(named / 512, (input.Length / 512) - (named / 512) + 1), drafts.Select(drafted => drafted.Received / 512));
        Assert.IsType<ItemStarted>(talk.Events.SkipWhile(happened => happened is not ItemDrafted).SkipWhile(happened => happened is ItemDrafted).First());
    }

    [Theory]
    [InlineData("Write", """{ "file_path": "/home/ana/.claude-work/plans/next.md", "content": "# Plan" }""")]
    [InlineData("Read", """{ "file_path": "/work/notes.md" }""")]
    [InlineData("Bash", """{ "command": "dotnet test" }""")]
    public void AToolThatWritesThePlanOrEditsNoFileIsNeverDrafted(string tool, string input)
    {
        var onHost = Cli.OnHost(input);
        var talk = new Talk().Begin().Receive(Cli.DraftedTool("m1", 1, "t1", tool, onHost));

        Assert.Empty(talk.Events.OfType<ItemDrafted>());
    }
}
