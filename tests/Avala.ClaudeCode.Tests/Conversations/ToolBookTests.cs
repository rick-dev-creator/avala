using System.Text.Json.Nodes;
using Avala.ClaudeCode.Conversations;
using Avala.ClaudeCode.Protocol;

namespace Avala.ClaudeCode.Tests.Conversations;

public sealed class ToolBookTests
{
    [Fact]
    public void ALongSessionRemembersOnlyItsLatestFinishedBackgroundToolsForAnotherRun()
    {
        var book = new ToolBook([]);

        foreach (var index in Enumerable.Range(0, ToolBook.Remembered + 50))
        {
            var tool = book.Track(new ToolUse($"a{index}", "Agent", []));
            tool.Backgrounded = true;
            tool.Closed = true;
            book.Forget();
        }

        Assert.Equal(
            (ToolBook.Remembered, false, true),
            (book.Remembers, book.Rerun("a0").IsSome, book.Rerun($"a{ToolBook.Remembered + 49}").IsSome));
    }

    [Fact]
    public void ABackgroundToolThatRanAgainAndFinishedIsRememberedOverOlderFinishedTools()
    {
        var book = new ToolBook([]);
        Finish(book, "a1");
        var again = book.Rerun("a1").Match(found => found, () => throw new InvalidOperationException("a1 was not remembered"));

        foreach (var index in Enumerable.Range(0, ToolBook.Remembered))
        {
            Finish(book, $"t{index}");
        }

        again.Closed = true;
        book.Forget();

        Assert.Equal((true, false), (book.Finished("a1", string.Empty), book.Finished("t0", string.Empty)));
    }

    private static void Finish(ToolBook book, string id)
    {
        var tool = book.Track(new ToolUse(id, "Agent", []));
        tool.Backgrounded = true;
        tool.Closed = true;
        book.Forget();
    }
}
