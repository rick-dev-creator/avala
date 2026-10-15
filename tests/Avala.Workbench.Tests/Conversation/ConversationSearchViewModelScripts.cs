using Avala.Workbench.Conversation;

namespace Avala.Workbench.Tests.Conversation;

public sealed class ConversationSearchViewModelScripts
{
    private readonly List<string> texts = ["Add the refund endpoint", "Ran dotnet test", "Refunds now round to whole yen", "Partial REFUND covered"];
    private readonly List<SearchFound> found = [];

    [Fact]
    public void TypingFindsEveryMatchingRowIgnoringCaseAndMakesTheFirstCurrent()
    {
        var search = Search();

        search.OpenCommand.Execute(null);
        search.Query = "refund";

        Assert.Equal(("1 of 3", true, 0), (search.Status, search.HasMatches, search.Revealed));
        Assert.Equal([0, 2, 3], found[^1].Matches);
        Assert.Equal(0, found[^1].Current);
    }

    [Fact]
    public void NextAndPreviousStepThroughTheMatchesAroundTheEnds()
    {
        var search = Search();
        search.OpenCommand.Execute(null);
        search.Query = "refund";

        search.NextCommand.Execute(null);
        search.NextCommand.Execute(null);
        search.NextCommand.Execute(null);
        var wrapped = search.Status;
        search.PreviousCommand.Execute(null);

        Assert.Equal("1 of 3", wrapped);
        Assert.Equal(("3 of 3", 3), (search.Status, search.Revealed));
    }

    [Fact]
    public void ClosingClearsTheMatchesAndATextFoundNowhereSaysSo()
    {
        var search = Search();
        search.OpenCommand.Execute(null);
        search.Query = "kubernetes";
        var nowhere = (search.Status, search.HasMatches, search.Revealed);
        search.Query = "refund";

        search.CloseCommand.Execute(null);

        Assert.Equal(("No matches", false, -1), nowhere);
        Assert.Equal((false, string.Empty, -1), (search.IsOpen, search.Status, search.Revealed));
        Assert.Empty(found[^1].Matches);
    }

    [Fact]
    public void ARowThatArrivesWhileTheBarIsOpenIsFoundToo()
    {
        var search = Search();
        search.OpenCommand.Execute(null);
        search.Query = "refund";

        texts.Add("Refund screen added");
        search.Refresh();

        Assert.Equal(("1 of 4", 4), (search.Status, found[^1].Matches[^1]));
    }

    private ConversationSearchViewModel Search()
    {
        var search = new ConversationSearchViewModel(() => texts);
        search.Found += (_, result) => found.Add(result);

        return search;
    }
}
