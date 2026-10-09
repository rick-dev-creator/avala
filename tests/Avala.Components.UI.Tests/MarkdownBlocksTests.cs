using Avala.Components.UI.Markdown;

namespace Avala.Components.UI.Tests;

public sealed class MarkdownBlocksTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("A paragraph still growing", 0)]
    [InlineData("First paragraph.\n\nSecond, still growing", 18)]
    [InlineData("# Title\n\n- one\n- two\n\nTail", 22)]
    [InlineData("Intro\n\n```go\nfunc A() {\n\n    return\n", 7)]
    [InlineData("Intro\n\n```go\nfunc A() {\n\n}\n```\n\nAfter", 32)]
    [InlineData("Intro\n\n~~~\na\n\n~~~\n\nAfter", 19)]
    [InlineData("Intro\n\n    ```\n\nIndented fence is code, not a fence", 16)]
    public void TheSettledPartEndsAtTheLastBlankLineOutsideAFence(string text, int settled) =>
        Assert.Equal(settled, MarkdownBlocks.Settled(text));
}
