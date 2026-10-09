using System.Globalization;
using Avala.Sdk;

namespace Avala.Runtime.Updates;

internal sealed record ReleaseVersion(int Major, int Minor, int Patch, string Prerelease)
{
    public static ReleaseVersion Zero { get; } = new(0, 0, 0, string.Empty);

    public bool IsPrerelease => Prerelease.Length > 0;

    public static Option<ReleaseVersion> Parse(string text)
    {
        var bare = text.Trim().TrimStart('v', 'V').Split('+', 2)[0];
        var parts = bare.Split('-', 2);

        return parts[0].Split('.') is [var major, var minor, var patch]
            && Number(major) is { } a
            && Number(minor) is { } b
            && Number(patch) is { } c
            && (parts.Length == 1 || parts[1].Split('.').All(identifier => identifier.Length > 0))
                ? new ReleaseVersion(a, b, c, parts.Length == 2 ? parts[1] : string.Empty)
                : Option<ReleaseVersion>.None;
    }

    public static int Compare(ReleaseVersion left, ReleaseVersion right) =>
        (left.Major, left.Minor, left.Patch).CompareTo((right.Major, right.Minor, right.Patch)) is var core and not 0
            ? core
            : (left.IsPrerelease, right.IsPrerelease) switch
            {
                (false, false) => 0,
                (false, true) => 1,
                (true, false) => -1,
                _ => Identifiers(left.Prerelease.Split('.'), right.Prerelease.Split('.')),
            };

    public bool IsNewerThan(ReleaseVersion other) => Compare(this, other) > 0;

    public override string ToString() =>
        IsPrerelease ? $"{Major}.{Minor}.{Patch}-{Prerelease}" : $"{Major}.{Minor}.{Patch}";

    private static int Identifiers(string[] left, string[] right) =>
        left.Zip(right, Identifier).FirstOrDefault(order => order != 0) is var order and not 0
            ? order
            : left.Length.CompareTo(right.Length);

    private static int Identifier(string left, string right) =>
        (Number(left), Number(right)) switch
        {
            ({ } a, { } b) => a.CompareTo(b),
            ({ }, null) => -1,
            (null, { }) => 1,
            _ => Math.Sign(string.CompareOrdinal(left, right)),
        };

    private static int? Number(string text) =>
        text.Length > 0 && text.All(char.IsAsciiDigit) && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
}
