namespace Avala.ArchitectureTests.Views;

internal enum ViewRule
{
    ViewForEveryViewModel,
    ViewModelForEveryView,
    CompiledBindings,
    DesignTimeDataContext,
    ViewModelInterfaces,
    DesignTimeImplementations,
    NoUiFrameworkInViewModels,
    PresentationOnlyCodeBehind,
    ComponentSize,
    DeclaredRegions,
    NoParentOrSiblingReferences,
    ScriptedAcceptanceTests,
    ThemeResourcesOnly,
    CommandsNotEventHandlers,
    NamedIconButtons,
    TypedRegionReferences,
}
