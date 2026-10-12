using System.Xml.Linq;
using Avala.Rendering.Animation;

namespace Avala.Rendering.Tests.Animation;

public sealed class SvgTimelineTests
{
    [Fact]
    public void ADrawingWithoutAnimationsHasNoTimeline() =>
        Assert.True(SvgTimeline.Of(Svg("<rect width=\"10\" height=\"10\"/>")).IsNone);

    [Theory]
    [InlineData("<animate attributeName=\"height\" to=\"9\" dur=\"2s\" fill=\"freeze\"/>", 2000, false)]
    [InlineData("<animate attributeName=\"height\" to=\"9\" begin=\"500ms\" dur=\"1.5\" repeatCount=\"2\"/>", 3500, false)]
    [InlineData("<animate attributeName=\"height\" to=\"9\" dur=\"00:01\" repeatCount=\"5\" repeatDur=\"2.5s\"/>", 2500, false)]
    [InlineData("<animate attributeName=\"height\" to=\"9\" dur=\"4s\" end=\"3s\"/>", 3000, false)]
    [InlineData("<animate id=\"a\" attributeName=\"height\" to=\"9\" dur=\"1s\"/><animate attributeName=\"width\" to=\"9\" begin=\"a.end+0.5s\" dur=\"1s\"/>", 2500, false)]
    [InlineData("<animateTransform attributeName=\"transform\" type=\"rotate\" to=\"360\" dur=\"2s\" repeatCount=\"indefinite\"/>", 1999, true)]
    [InlineData("<set attributeName=\"fill\" to=\"red\" begin=\"1min\"/>", 60000, false)]
    [InlineData("<animate attributeName=\"height\" to=\"9\" begin=\"click\" dur=\"2s\"/><animate attributeName=\"width\" to=\"9\" dur=\"1s\"/>", 1000, false)]
    public void TheEndIsWhereTheLongestAnimationSettles(string animations, int milliseconds, bool loops)
    {
        var timeline = SvgTimeline.Of(Svg($"<rect width=\"10\" height=\"10\">{animations}</rect>")).Match(found => found, () => throw new Xunit.Sdk.XunitException("No timeline."));

        Assert.Equal((TimeSpan.FromMilliseconds(milliseconds), loops), (timeline.End, timeline.Loops));
    }

    private static XElement Svg(string body) =>
        XElement.Parse($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\">{body}</svg>");
}
