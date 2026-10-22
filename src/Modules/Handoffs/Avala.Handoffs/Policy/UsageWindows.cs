using System.Globalization;

namespace Avala.Handoffs.Policy;

internal static class UsageWindows
{
    public static readonly TimeSpan Fallback = TimeSpan.FromHours(5);

    public static TimeSpan LengthOf(string window)
    {
        var digits = new string([.. window.TakeWhile(char.IsAsciiDigit)]);

        if (digits.Length == 0 || digits.Length == window.Length || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count <= 0)
        {
            return Fallback;
        }

        return char.ToLowerInvariant(window[digits.Length]) switch
        {
            'm' => TimeSpan.FromMinutes(count),
            'h' => TimeSpan.FromHours(count),
            'd' => TimeSpan.FromDays(count),
            _ => Fallback,
        };
    }
}
