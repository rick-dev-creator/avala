using Avala.ArchitectureTests.Scopes;
using Avala.ArchitectureTests.Solution;
using Avala.Sdk.Regions;

namespace Avala.ArchitectureTests.Views;

internal static class ViewFindings
{
    public static async Task<IReadOnlyList<Finding>> OfAsync(Scope which, ViewRule rule, CancellationToken cancellationToken)
    {
        var scope = ViewScope.Of(which);

        return rule switch
        {
            ViewRule.ViewForEveryViewModel => [.. ViewModelRules.WithoutView(scope)],
            ViewRule.ViewModelForEveryView => [.. ViewModelRules.WithoutViewModel(scope)],
            ViewRule.ViewModelInterfaces => [.. ViewModelRules.WithoutInterface(scope)],
            ViewRule.DesignTimeImplementations => [.. ViewModelRules.WithoutDesignTimeImplementation(scope)],
            ViewRule.NoUiFrameworkInViewModels => [.. ViewModelRules.InUiFrameworkAssemblies(scope)],
            ViewRule.NoParentOrSiblingReferences => [.. ViewModelRules.ReferencingParentOrSibling(scope)],
            ViewRule.DeclaredRegions => [.. ViewModelRules.ConstructRegionNamesOutsideDeclarations(scope)],
            ViewRule.CompiledBindings => [.. XamlRules.WithoutDataType(await ViewsAsync(scope, cancellationToken))],
            ViewRule.DesignTimeDataContext => [.. XamlRules.WithoutDesignDataContext(await ViewsAsync(scope, cancellationToken))],
            ViewRule.ThemeResourcesOnly => [.. XamlRules.HardCodedStyling(await ViewsAsync(scope, cancellationToken), await ThemeCatalog.ReadAsync(cancellationToken))],
            ViewRule.CommandsNotEventHandlers => [.. XamlRules.EventHandlers(await ViewsAsync(scope, cancellationToken))],
            ViewRule.NamedIconButtons => [.. XamlRules.UnnamedIconButtons(await ViewsAsync(scope, cancellationToken))],
            ViewRule.TypedRegionReferences => [.. XamlRules.RegionsByString(await ViewsAsync(scope, cancellationToken), RegionKeys(scope))],
            ViewRule.PresentationOnlyCodeBehind => [.. (await SourceViewRules.CodeBehindAsync(scope, cancellationToken)).Where(finding => finding.Rule == rule)],
            ViewRule.ComponentSize =>
            [
                .. XamlRules.Oversized(await XamlFile.ReadAllAsync(scope.FilesUnderSources("*.axaml"), cancellationToken)),
                .. (await SourceViewRules.CodeBehindAsync(scope, cancellationToken)).Where(finding => finding.Rule == rule),
                .. await SourceViewRules.OversizedViewModelsAsync(scope, cancellationToken),
            ],
            ViewRule.ScriptedAcceptanceTests => [.. await SourceViewRules.WithoutScriptsAsync(scope, cancellationToken)],
            _ => throw new ArgumentOutOfRangeException(nameof(rule)),
        };
    }

    private static async Task<IReadOnlyList<XamlFile>> ViewsAsync(ViewScope scope, CancellationToken cancellationToken) =>
        [.. (await XamlFile.ReadAllAsync(scope.FilesUnderSources("*.axaml"), cancellationToken)).Where(file => file.IsView)];

    private static HashSet<string> RegionKeys(ViewScope scope) =>
        [
            .. AvalaAssemblies.AllTypes.Concat(scope.Types)
                .Where(type => type is { IsAbstract: true, IsSealed: true })
                .SelectMany(type => type.GetProperties())
                .Where(property => property.PropertyType == typeof(RegionName) && property.GetMethod is { IsStatic: true })
                .Select(property => ((RegionName)property.GetValue(null)!).Key),
        ];
}
