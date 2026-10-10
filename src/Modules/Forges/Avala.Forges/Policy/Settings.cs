using Avala.Forges.Contracts;
using Avala.Sdk;

namespace Avala.Forges.Policy;

internal sealed record ForgeDeclaration(ForgeName Name, string Forge, Option<Uri> Url, CredentialSource Source, Option<string> Reference);

internal sealed record ForgeSettings(IReadOnlyList<ForgeDeclaration> Forges, TimeSpan Poll)
{
    public static TimeSpan DefaultPoll { get; } = TimeSpan.FromSeconds(60);

    public static TimeSpan ShortestPoll { get; } = TimeSpan.FromSeconds(15);

    public static TimeSpan LongestPoll { get; } = TimeSpan.FromHours(1);

    public static ForgeSettings None { get; } = new([], DefaultPoll);

    public Option<ForgeDeclaration> Find(ForgeName name) => Forges.FirstOrDefault(forge => forge.Name == name).ToOption();
}

internal sealed record PullRequestRules(ForgeName Forge, string Remote, OnPullRequest Policy, int MaxWakeUps)
{
    public const string DefaultRemote = "origin";

    public const int DefaultWakeUps = 3;

    public const int MostWakeUps = 20;
}

internal static class ForgeNames
{
    public const int Longest = 64;

    public static bool IsValid(string name) =>
        name.Length is > 0 and <= Longest
        && char.IsAsciiLetterOrDigit(name[0])
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}
