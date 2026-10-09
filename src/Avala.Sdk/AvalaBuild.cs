namespace Avala.Sdk;

public sealed record AvalaBuild(string Version, Option<string> Commit)
{
    public const string Unknown = "unknown";

    public static Uri Repository { get; } = new("https://github.com/rick-dev-creator/avala");

    public static Uri License { get; } = new("https://github.com/rick-dev-creator/avala/blob/main/LICENSE");

    public static AvalaBuild From(Option<string> informationalVersion) =>
        informationalVersion.Match(
            text => text.Split('+', 2) is [{ Length: > 0 } version, { Length: > 0 } commit]
                ? new AvalaBuild(version, commit)
                : new AvalaBuild(text.TrimEnd('+') is { Length: > 0 } bare ? bare : Unknown, Option<string>.None),
            () => new AvalaBuild(Unknown, Option<string>.None));
}
