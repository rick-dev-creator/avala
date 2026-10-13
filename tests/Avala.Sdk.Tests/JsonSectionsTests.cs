namespace Avala.Sdk.Tests;

public sealed class JsonSectionsTests
{
    private enum Failure
    {
        Malformed,
    }

    [Fact]
    public void ASectionIsReadWhateverTheDepthOfTheOtherSections() =>
        Assert.Equal("on", Read("""{ "mine": { "mode": "on" }, "theirs": { "lists": [[{ "deep": [1] }]] } }"""));

    [Fact]
    public void ASectionDeeperThanItsBoundIsMalformed() =>
        Assert.Equal("Malformed", Read("""{ "mine": { "mode": ["on"] } }"""));

    [Fact]
    public void AnAbsentSectionIsReadAsNothing() =>
        Assert.Equal("absent", Read("""{ "theirs": { "lists": [[1]] } }"""));

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "mine": { "mode": "on" }, "theirs": 1, "theirs": 2 }""")]
    public void AFileThatIsNotOneJsonObjectIsMalformed(string text) =>
        Assert.Equal("Malformed", Read(text));

    private static string Read(string text) =>
        JsonSections.Read(text, "mine", 1, Failure.Malformed, section => Result<string, Failure>.Success(section.Match(found => found.GetProperty("mode").ToString(), () => "absent")))
            .Match(value => value, failure => failure.ToString());
}
