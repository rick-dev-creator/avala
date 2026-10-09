namespace Avala.ArchitectureTests.Views;

internal static class PendingRetrofit
{
    public static IReadOnlySet<(ViewRule Rule, string Module)> Ceiling { get; } = new HashSet<(ViewRule, string)>();

    public static IReadOnlySet<(ViewRule Rule, string Module)> Scoped { get; } = new HashSet<(ViewRule, string)>();

    public static bool Exempts(Finding finding) => Scoped.Contains((finding.Rule, finding.Module));
}
