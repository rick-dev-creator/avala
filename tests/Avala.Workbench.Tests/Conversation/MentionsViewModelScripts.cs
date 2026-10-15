using Avala.Workbench.Conversation;

namespace Avala.Workbench.Tests.Conversation;

public sealed class MentionsViewModelScripts
{
    private readonly MentionsViewModel mentions = Composing.Mentioning(new Worktree(), "src/Checkout/Rounding.cs", "src/Checkout/Prices.cs", "docs/rounding.md");

    [Fact]
    public async Task TypingAnAtOpensTheSuggestionsAndTheArrowsMoveTheSelectionAroundAsync()
    {
        mentions.Look("Check @round");
        await mentions.Looking;

        mentions.NextCommand.Execute(null);
        var second = mentions.Suggestions.Select(row => row.IsSelected).ToList();
        mentions.NextCommand.Execute(null);

        Assert.True(mentions.IsOpen);
        Assert.Equal(["docs/rounding.md", "src/Checkout/Rounding.cs"], mentions.Suggestions.Select(row => row.Label));
        Assert.Equal([false, true], second);
        Assert.Equal([true, false], mentions.Suggestions.Select(row => row.IsSelected));
    }

    [Fact]
    public async Task ChoosingASuggestionWritesItsTokenInPlaceOfTheQueryAsync()
    {
        var written = new List<string>();
        mentions.Chosen += (_, chosen) => written.Add(chosen.Draft);
        mentions.Look("Check @Pri");
        await mentions.Looking;

        mentions.ChooseCommand.Execute(null);

        Assert.Equal(["Check @src/Checkout/Prices.cs "], written);
        Assert.False(mentions.IsOpen);
    }

    [Fact]
    public async Task AnAtInsideAWordOrAQueryNothingMatchesShowsNoSuggestionsAsync()
    {
        mentions.Look("Mail ana@round");
        await mentions.Looking;
        var inWord = mentions.IsOpen;
        mentions.Look("@zzz");
        await mentions.Looking;

        Assert.Equal((false, false), (inWord, mentions.IsOpen));
    }

    [Fact]
    public async Task EscapingClosesTheSuggestionsAndLeavesTheTextAsItIsAsync()
    {
        mentions.Look("@round");
        await mentions.Looking;

        mentions.DismissCommand.Execute(null);

        Assert.Equal((false, 0), (mentions.IsOpen, mentions.Suggestions.Count));
    }

    [Fact]
    public async Task OnlyTheChosenMentionsStillInTheTextAreReferencedAsync()
    {
        mentions.Look("@Pri");
        await mentions.Looking;
        mentions.ChooseCommand.Execute(null);
        mentions.Look("@Rou");
        await mentions.Looking;
        mentions.ChooseCommand.Execute(null);

        var sent = mentions.References("Keep @src/Checkout/Prices.cs as it is");

        Assert.Equal("Keep @src/Checkout/Prices.cs as it is\n\nReferenced:\n- @src/Checkout/Prices.cs: the file src/Checkout/Prices.cs in the job's worktree", sent);
    }
}

public sealed class MentionRowViewModelScripts
{
    [Fact]
    public async Task ClickingARowChoosesItsMentionAsync()
    {
        var mentions = Composing.Mentioning(new Worktree(), "src/Checkout/Rounding.cs", "src/Checkout/Prices.cs");
        var written = new List<string>();
        mentions.Chosen += (_, chosen) => written.Add(chosen.Draft);
        mentions.Look("@src");
        await mentions.Looking;

        mentions.Suggestions[1].ChooseCommand.Execute(null);

        Assert.Equal(["@src/Checkout/Prices.cs "], written);
    }
}
