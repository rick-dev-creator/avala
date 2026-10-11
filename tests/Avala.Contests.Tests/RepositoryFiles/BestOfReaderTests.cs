using Avala.Contests.Contracts;
using Avala.Contests.RepositoryFiles;
using Avala.Testing;

namespace Avala.Contests.Tests.RepositoryFiles;

public sealed class BestOfReaderTests
{
    [Theory]
    [InlineData("""{ "approval": "merge" }""", 1, 4)]
    [InlineData("""{ "bestOf": {} }""", 1, 4)]
    [InlineData("""{ "bestOf": { "candidates": 3 } }""", 3, 4)]
    [InlineData("""{ "bestOf": { "candidates": 2, "max": 2 } }""", 2, 2)]
    [InlineData("""{ "connection": "work", "bestOf": { "candidates": 6, "max": 8 } }""", 6, 8)]
    [InlineData("""{ "bestOf": { "max": 1 } }""", 1, 1)]
    public void ADeclarationNamesTheCandidatesAJobStartsWithAndTheMostAContestMayHave(string json, int candidates, int max) =>
        Assert.Equal(new BestOf(candidates, max), Outcomes.Succeeds(BestOfReader.Parse(json)));

    [Theory]
    [InlineData("""[]""", ContestRejection.Malformed)]
    [InlineData("""{ "bestOf": 3 }""", ContestRejection.Malformed)]
    [InlineData("""{ "bestOf": { "candidates": "three" } }""", ContestRejection.Malformed)]
    [InlineData("""{ "bestOf": { "candidates": 2, "candidates": 3 } }""", ContestRejection.Malformed)]
    [InlineData("""{ "bestOf": { "judge": "llm" } }""", ContestRejection.UnknownField)]
    [InlineData("""{ "bestOf": { "candidates": 0 } }""", ContestRejection.InvalidCandidates)]
    [InlineData("""{ "bestOf": { "candidates": 5 } }""", ContestRejection.InvalidCandidates)]
    [InlineData("""{ "bestOf": { "candidates": 2.5 } }""", ContestRejection.InvalidCandidates)]
    [InlineData("""{ "bestOf": { "max": 9 } }""", ContestRejection.InvalidMax)]
    [InlineData("""{ "bestOf": { "max": 0 } }""", ContestRejection.InvalidMax)]
    public void AnInvalidDeclarationIsRejectedWithItsError(string json, ContestRejection expected) =>
        Assert.Equal(expected, Outcomes.FailsWith(BestOfReader.Parse(json)));

    [Fact]
    public void AFileOverItsSizeIsRejected() =>
        Assert.Equal(ContestRejection.TooLarge, Outcomes.FailsWith(BestOfReader.Parse($$"""{ "note": "{{new string('x', BestOfReader.MaximumBytes)}}" }""")));
}
