using Avala.Rendering.Animation;
using Avala.Rendering.Sizing;
using Avala.Sdk;

namespace Avala.Rendering.Sanitizing;

internal sealed record SanitizedSvg(string Markup, int Blocked)
{
    public Option<SvgTimeline> Timeline { get; init; }

    public Option<SvgArea> Area { get; init; }
}
