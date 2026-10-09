namespace Avala.ArchitectureTests.Views;

internal static class PendingRetrofit
{
    private const string Workbench = "Workbench";

    public static IReadOnlySet<(ViewRule Rule, string Module)> Ceiling { get; } = new HashSet<(ViewRule, string)>
    {
        (ViewRule.ViewModelInterfaces, Workbench),
        (ViewRule.DesignTimeDataContext, Workbench),
        (ViewRule.ScriptedAcceptanceTests, Workbench),
        (ViewRule.ThemeResourcesOnly, Workbench),
    };

    public static IReadOnlySet<(ViewRule Rule, string Module)> Scoped { get; } = new HashSet<(ViewRule, string)>
    {
        (ViewRule.ViewModelInterfaces, Workbench),
        (ViewRule.DesignTimeDataContext, Workbench),
        (ViewRule.ScriptedAcceptanceTests, Workbench),
        (ViewRule.ThemeResourcesOnly, Workbench),
    };

    public static bool Exempts(Finding finding) => Scoped.Contains((finding.Rule, finding.Module));
}
