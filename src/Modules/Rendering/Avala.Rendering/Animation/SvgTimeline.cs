using System.Globalization;
using System.Xml.Linq;
using Avala.Sdk;

namespace Avala.Rendering.Animation;

internal sealed record SvgTimeline(TimeSpan End, bool Loops)
{
    private static readonly HashSet<string> Animations = new(StringComparer.OrdinalIgnoreCase)
    {
        "animate", "set", "animateMotion", "animateTransform", "animateColor",
    };

    private static readonly TimeSpan LastFrame = TimeSpan.FromMilliseconds(1);

    public static Option<SvgTimeline> Of(XElement root)
    {
        var animations = root.Descendants().Where(element => Animations.Contains(element.Name.LocalName)).ToList();

        if (animations.Count == 0)
        {
            return Option<SvgTimeline>.None;
        }

        var byId = animations
            .Where(animation => animation.Attribute("id") is not null)
            .GroupBy(animation => animation.Attribute("id")!.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var spans = animations.Select(animation => Span(animation, byId, [])).OfType<Interval>().ToList();

        return spans.Count == 0
            ? new SvgTimeline(TimeSpan.Zero, Loops: false)
            : new SvgTimeline(spans.Max(span => span.Rest), spans.Any(span => span.Repeats));
    }

    private static Interval? Span(XElement animation, Dictionary<string, XElement> byId, HashSet<XElement> resolving)
    {
        if (!resolving.Add(animation))
        {
            return null;
        }

        try
        {
            var begin = Begin(animation, byId, resolving);
            var duration = Clock(Value(animation, "dur"));
            var repeats = Value(animation, "repeatCount");
            var repeatFor = Value(animation, "repeatDur");
            var loops = Indefinite(repeats) || Indefinite(repeatFor);

            if (begin is not { } start)
            {
                return null;
            }

            if (duration is not { } cycle)
            {
                return new Interval(start, start, start, Repeats: false);
            }

            var active = loops ? cycle : Active(cycle, repeats, repeatFor);
            var end = Clock(Value(animation, "end")) is { } until && until > start && until < start + active ? until : start + active;

            return new Interval(start, end, loops ? end - LastFrame : end, loops);
        }
        finally
        {
            resolving.Remove(animation);
        }
    }

    private static TimeSpan? Begin(XElement animation, Dictionary<string, XElement> byId, HashSet<XElement> resolving)
    {
        var begin = Value(animation, "begin");

        if (begin.Length == 0)
        {
            return TimeSpan.Zero;
        }

        var starts = begin.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(value => Offset(value, byId, resolving))
            .OfType<TimeSpan>()
            .ToList();

        return starts.Count == 0 ? null : starts.Min();
    }

    private static TimeSpan? Offset(string value, Dictionary<string, XElement> byId, HashSet<XElement> resolving)
    {
        if (Clock(value) is { } offset)
        {
            return offset;
        }

        var dot = value.IndexOf('.', StringComparison.Ordinal);

        if (dot <= 0)
        {
            return null;
        }

        var sign = value.IndexOfAny(['+', '-'], dot);
        var reference = sign < 0 ? value : value[..sign].Trim();
        var shift = sign < 0 ? TimeSpan.Zero : Clock(value[(sign + 1)..]);

        if (shift is not { } amount || !byId.TryGetValue(value[..dot], out var other) || Span(other, byId, resolving) is not { } span)
        {
            return null;
        }

        var delay = sign >= 0 && value[sign] == '-' ? -amount : amount;

        return reference[(dot + 1)..] switch
        {
            "begin" => span.Start + delay,
            "end" => span.Finish + delay,
            _ => null,
        };
    }

    private static TimeSpan Active(TimeSpan cycle, string repeats, string repeatFor)
    {
        var counted = double.TryParse(repeats, NumberStyles.Float, CultureInfo.InvariantCulture, out var count) && count > 0 ? cycle * count : (TimeSpan?)null;
        var limited = Clock(repeatFor);

        return (counted, limited) switch
        {
            ({ } times, { } length) => times < length ? times : length,
            ({ } times, null) => times,
            (null, { } length) => length,
            _ => cycle,
        };
    }

    private static TimeSpan? Clock(string value)
    {
        var text = value.Trim();

        if (text.Contains(':', StringComparison.Ordinal))
        {
            var parts = text.Split(':');
            var seconds = 0d;

            foreach (var part in parts)
            {
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number < 0)
                {
                    return null;
                }

                seconds = seconds * 60 + number;
            }

            return parts.Length <= 3 ? TimeSpan.FromSeconds(seconds) : null;
        }

        var (digits, scale) = text switch
        {
            _ when text.EndsWith("ms", StringComparison.Ordinal) => (text[..^2], 0.001),
            _ when text.EndsWith("min", StringComparison.Ordinal) => (text[..^3], 60d),
            _ when text.EndsWith('h') => (text[..^1], 3600d),
            _ when text.EndsWith('s') => (text[..^1], 1d),
            _ => (text, 1d),
        };

        return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) && amount >= 0 && double.IsFinite(amount)
            ? TimeSpan.FromSeconds(amount * scale)
            : null;
    }

    private static bool Indefinite(string value) => value.Equals("indefinite", StringComparison.Ordinal);

    private static string Value(XElement element, string name) => element.Attribute(name)?.Value.Trim() ?? string.Empty;

    private sealed record Interval(TimeSpan Start, TimeSpan Finish, TimeSpan Rest, bool Repeats);
}
