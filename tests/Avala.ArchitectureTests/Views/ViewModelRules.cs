using System.Reflection;
using Avala.Sdk.Regions;
using ArchUnitNET.Domain.Dependencies;

namespace Avala.ArchitectureTests.Views;

internal static class ViewModelRules
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly string[] UiFrameworks =
    [
        "Avalonia", "Microsoft.Maui", "Uno", "Uno.UI", "PresentationFramework", "PresentationCore", "WindowsBase",
        "System.Windows.Forms", "Microsoft.WinUI", "Microsoft.UI.Xaml", "Microsoft.AspNetCore.Components",
        "Terminal.Gui", "Xamarin.Forms", "Eto", "GtkSharp", "Gtk",
    ];

    public static IEnumerable<Finding> WithoutView(ViewScope scope)
    {
        var views = scope.Types.Where(type => type.IsView).Select(type => type.Stem).ToHashSet(StringComparer.Ordinal);

        return scope.Types.Where(type => type.IsViewModel && !views.Contains(type.Stem))
            .Select(type => Finding.OfType(ViewRule.ViewForEveryViewModel, type, $"has no {type.Stem}View; add the view to the module's .UI assembly in a folder named {type.Stem}View"));
    }

    public static IEnumerable<Finding> WithoutViewModel(ViewScope scope)
    {
        var viewModels = scope.Types.Where(type => type.IsViewModel).Select(type => type.Stem).ToHashSet(StringComparer.Ordinal);

        return scope.Types.Where(type => type.IsView && !viewModels.Contains(type.Stem))
            .Select(type => Finding.OfType(ViewRule.ViewModelForEveryView, type, $"has no {type.Stem}ViewModel; a view only shows its own view model"));
    }

    public static IEnumerable<Finding> WithoutInterface(ViewScope scope) =>
        scope.Types.Where(type => type.IsViewModel && !type.GetInterfaces().Any(contract => contract.Name == $"I{type.Name}"))
            .Select(type => Finding.OfType(ViewRule.ViewModelInterfaces, type, $"implements no I{type.Name}; declare the interface its view binds to"));

    public static IEnumerable<Finding> WithoutDesignTimeImplementation(ViewScope scope)
    {
        var designs = scope.Types.Where(type => type.IsDesignViewModel).ToList();

        return scope.Types.Where(type => type.IsViewModelInterface)
            .Where(contract => !designs.Any(design => design.Name == $"{ComponentKinds.DesignPrefix}{contract.Name[1..]}" && contract.IsAssignableFrom(design)))
            .Select(contract => Finding.OfType(
                ViewRule.DesignTimeImplementations,
                contract,
                $"has no {ComponentKinds.DesignPrefix}{contract.Name[1..]}; add a design-time implementation with realistic data"));
    }

    public static IEnumerable<Finding> InUiFrameworkAssemblies(ViewScope scope) =>
        scope.Types.Where(type => type.IsViewModel || type.IsViewModelInterface || type.IsDesignViewModel)
            .Select(type => (Type: type, Framework: UiFrameworkOf(type.Assembly)))
            .Where(found => found.Framework is not null)
            .Select(found => Finding.OfType(
                ViewRule.NoUiFrameworkInViewModels,
                found.Type,
                $"lives in {found.Type.Assembly.GetName().Name}, which references {found.Framework}; move it to the module's core assembly, which knows no UI framework"));

    public static IEnumerable<Finding> ReferencingParentOrSibling(ViewScope scope)
    {
        var viewModels = scope.Types.Where(type => type.IsViewModel).ToList();
        var children = viewModels.ToDictionary(type => type, type => ReferencedViewModels(type, viewModels));

        return viewModels.SelectMany(child =>
        {
            var parents = viewModels.Where(parent => parent != child && children[parent].Contains(child)).ToList();

            return children[child]
                .Where(target => target != child)
                .Select(target => parents.Contains(target)
                    ? $"references its parent {target.Name}; report upward through a command, an event or a message"
                    : parents.FirstOrDefault(parent => children[parent].Contains(target)) is { } shared
                        ? $"references {target.Name}, a sibling under {shared.Name}; let {shared.Name} pass data down, or publish a UI message"
                        : null)
                .OfType<string>()
                .Select(problem => Finding.OfType(ViewRule.NoParentOrSiblingReferences, child, problem));
        });
    }

    public static IEnumerable<Finding> ConstructRegionNamesOutsideDeclarations(ViewScope scope)
    {
        var types = scope.Types.ToDictionary(type => type.FullName ?? type.Name, StringComparer.Ordinal);

        return scope.ArchitectureTypes
            .Where(type => type.FullName != typeof(RegionName).FullName)
            .Where(type => type.Dependencies.OfType<MethodCallDependency>().Any(call =>
                call.Target.FullName == typeof(RegionName).FullName && call.TargetMember.Name.StartsWith(".ctor", StringComparison.Ordinal)))
            .Select(type => types.GetValueOrDefault(type.FullName))
            .OfType<Type>()
            .Where(type => !(type is { IsAbstract: true, IsSealed: true } && type.Name.EndsWith("Regions", StringComparison.Ordinal)))
            .Select(type => Finding.OfType(
                ViewRule.DeclaredRegions,
                type,
                "creates a RegionName; declare every region once as a static property of a static class named <Page>Regions and refer to it from there"));
    }

    private static HashSet<Type> ReferencedViewModels(Type type, IReadOnlyList<Type> viewModels) =>
        [
            .. type.GetFields(InstanceFields)
                .SelectMany(field => Flatten(field.FieldType))
                .SelectMany(referenced => viewModels.Where(candidate =>
                    candidate.Assembly == type.Assembly && (candidate == referenced || (referenced.IsInterface && referenced.IsAssignableFrom(candidate))))),
        ];

    private static IEnumerable<Type> Flatten(Type type) =>
        [
            type,
            .. type.IsArray ? Flatten(type.GetElementType()!) : [],
            .. type.IsGenericType ? type.GetGenericArguments().SelectMany(Flatten) : [],
        ];

    private static string? UiFrameworkOf(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .FirstOrDefault(name => UiFrameworks.Any(framework =>
                name == framework || name.StartsWith($"{framework}.", StringComparison.Ordinal)));
}
