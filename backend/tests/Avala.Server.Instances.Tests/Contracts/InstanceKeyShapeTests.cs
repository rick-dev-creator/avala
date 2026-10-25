using Avala.Server.Instances.Contracts.Authentication;
using Avala.Server.Instances.Domain;

namespace Avala.Server.Instances.Tests.Contracts;

public sealed class InstanceKeyShapeTests
{
    private static readonly string Body = new('A', 43);

    public static TheoryData<string> OtherShapes =>
    [
        string.Empty,
        "avk_",
        "key_" + Body,
        "avk_" + Body[1..],
        "avk_" + Body + "A",
        "avk_" + Body[1..] + "+",
    ];

    [Fact]
    public void EveryIssuedKeyHasTheShapeOfAKey() =>
        Assert.All(Enumerable.Range(0, 100).Select(_ => InstanceKey.Issue()), key => Assert.True(InstanceKeyShape.Matches(key)));

    [Theory]
    [MemberData(nameof(OtherShapes))]
    public void TextOfAnotherShapeIsNotAKey(string text) =>
        Assert.False(InstanceKeyShape.Matches(text));
}
