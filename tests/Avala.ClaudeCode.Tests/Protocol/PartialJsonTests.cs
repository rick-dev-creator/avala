using Avala.ClaudeCode.Protocol;

namespace Avala.ClaudeCode.Tests.Protocol;

public sealed class PartialJsonTests
{
    [Fact]
    public void ValuesThatAreNotStringsAreSkippedEvenWhenTheyNestStringsThatLookLikeTheirEnd()
    {
        var found = PartialJson.Strings("""
            { "count": 3, "open": true, "meta": { "note": "a,}b", "list": [1, { "deep": "]" }] }, "title": "Plan", "content": "hel
            """);

        Assert.Equal(
            new Dictionary<string, PartialText> { ["title"] = new("Plan", true), ["content"] = new("hel", false) },
            found);
    }

    [Fact]
    public void ANestedValueCutOffMidwayKeepsTheMembersReadBeforeIt()
    {
        var found = PartialJson.Strings("""{ "title": "Plan", "meta": { "note": "a", "n": [1""");

        Assert.Equal(new Dictionary<string, PartialText> { ["title"] = new("Plan", true) }, found);
    }

    [Theory]
    [InlineData("""{ "content": "a\nb\tc\rd\be\ff\/g\"h\\i" }""", "a\nb\tc\rd\be\ff/g\"h\\i", true)]
    [InlineData("""{ "content": "café 😀" }""", "café 😀", true)]
    [InlineData("""{ "content": "ab\""", "ab", false)]
    [InlineData("""{ "content": "ab\u00""", "ab", false)]
    [InlineData("""{ "content": "ab\ud83d""", "ab", false)]
    [InlineData("""{ "content": "ab\ud83d\ude""", "ab", false)]
    public void EscapesAreDecodedAndAnEscapeCutOffMidwayLeavesTheTextIncomplete(string json, string text, bool complete)
    {
        Assert.Equal(new PartialText(text, complete), PartialJson.Strings(json)["content"]);
    }
}
