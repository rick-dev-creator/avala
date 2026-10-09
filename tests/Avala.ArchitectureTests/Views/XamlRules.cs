using System.Xml.Linq;

namespace Avala.ArchitectureTests.Views;

internal static class XamlRules
{
    public const int MaximumXamlLines = 800;

    private static readonly HashSet<string> StyledProperties =
    [
        "Background", "Foreground", "BorderBrush", "Fill", "Stroke", "Color", "TintColor", "FallbackColor", "CaretBrush",
        "SelectionBrush", "SelectionForegroundBrush", "FontSize", "FontFamily", "FontWeight", "CornerRadius", "BoxShadow",
    ];

    private static readonly HashSet<string> Events =
    [
        "Click", "Tapped", "DoubleTapped", "Holding", "PointerPressed", "PointerReleased", "PointerMoved", "PointerEntered",
        "PointerExited", "PointerWheelChanged", "PointerCaptureLost", "KeyDown", "KeyUp", "TextInput", "TextChanged", "TextChanging",
        "GotFocus", "LostFocus", "SelectionChanged", "Checked", "Unchecked", "Indeterminate", "IsCheckedChanged", "ValueChanged",
        "Loaded", "Unloaded", "Initialized", "AttachedToVisualTree", "DetachedFromVisualTree", "AttachedToLogicalTree",
        "DetachedFromLogicalTree", "DataContextChanged", "PropertyChanged", "LayoutUpdated", "SizeChanged", "EffectiveViewportChanged",
        "ScrollChanged", "ContextRequested", "Opened", "Opening", "Closed", "Closing", "DropDownOpened", "DropDownClosed",
        "ContainerPrepared", "ContainerClearing", "ElementPrepared", "ElementClearing", "Expanded", "Collapsed", "Expanding", "Collapsing",
    ];

    private static readonly HashSet<string> Buttons =
        ["Button", "ToggleButton", "RepeatButton", "SplitButton", "ToggleSplitButton", "DropDownButton", "HyperlinkButton"];

    private static readonly HashSet<string> TextElements = ["TextBlock", "SelectableTextBlock", "AccessText", "Run", "Label"];

    public static IEnumerable<Finding> WithoutDataType(IEnumerable<XamlFile> views) =>
        views.Where(view => view.Root.Attribute(XName.Get("DataType", XamlFile.XamlNamespace)) is null)
            .Select(view => Finding.OfFile(
                ViewRule.CompiledBindings,
                view.Path,
                XamlFile.LineOf(view.Root),
                "the view declares no x:DataType; add x:DataType=\"<namespace>:I<Name>ViewModel\" so its bindings are compiled"));

    public static IEnumerable<Finding> WithoutDesignDataContext(IEnumerable<XamlFile> views) =>
        views.Where(view => !HasDesignDataContext(view.Root))
            .Select(view => Finding.OfFile(
                ViewRule.DesignTimeDataContext,
                view.Path,
                XamlFile.LineOf(view.Root),
                "the view declares no design-time DataContext; add <Design.DataContext><vm:Design<Name>ViewModel /></Design.DataContext>"));

    public static IEnumerable<Finding> Oversized(IEnumerable<XamlFile> views) =>
        views.Where(view => view.LineCount > MaximumXamlLines)
            .Select(view => Finding.OfFile(
                ViewRule.ComponentSize,
                view.Path,
                1,
                $"{view.LineCount} lines, over the {MaximumXamlLines} a XAML file may have; split it into smaller components"));

    public static IEnumerable<Finding> HardCodedStyling(IEnumerable<XamlFile> views, ThemeCatalog theme) =>
        views.SelectMany(view => view.Root.DescendantsAndSelf().SelectMany(element => StyledValues(element)
            .Where(styled => !IsThemed(styled.Property, styled.Value))
            .Select(styled => Finding.OfFile(
                ViewRule.ThemeResourcesOnly,
                view.Path,
                XamlFile.LineOf(styled.Node),
                $"{styled.Property}=\"{styled.Value}\" is hard-coded; {theme.Suggest(styled.Property, styled.Value)}"))));

    public static IEnumerable<Finding> EventHandlers(IEnumerable<XamlFile> views) =>
        views.SelectMany(view => view.Root.DescendantsAndSelf().SelectMany(element => element.Attributes()
            .Where(attribute => attribute.Name.Namespace == XNamespace.None && Events.Contains(LastSegment(attribute.Name.LocalName)))
            .Select(attribute => Finding.OfFile(
                ViewRule.CommandsNotEventHandlers,
                view.Path,
                XamlFile.LineOf(attribute),
                $"{attribute.Name.LocalName}=\"{attribute.Value}\" handles an event in code-behind; bind a command of the view model instead, such as Command=\"{{Binding SaveCommand}}\""))));

    public static IEnumerable<Finding> UnnamedIconButtons(IEnumerable<XamlFile> views) =>
        views.SelectMany(view => view.Root.DescendantsAndSelf()
            .Where(element => Buttons.Contains(element.Name.LocalName) && IsIconOnly(element) && element.Attribute("AutomationProperties.Name") is null)
            .Select(element => Finding.OfFile(
                ViewRule.NamedIconButtons,
                view.Path,
                XamlFile.LineOf(element),
                $"this {element.Name.LocalName} shows only an icon; add AutomationProperties.Name=\"<what it does>\" so screen readers can name it")));

    public static IEnumerable<Finding> RegionsByString(IEnumerable<XamlFile> views, IReadOnlySet<string> regionKeys) =>
        views.SelectMany(view => view.Root.DescendantsAndSelf().SelectMany(element => element.Attributes()
            .Where(attribute => (LastSegment(attribute.Name.LocalName) is "Region" or "RegionName" || regionKeys.Contains(attribute.Value))
                && !attribute.Value.StartsWith("{x:Static ", StringComparison.Ordinal))
            .Select(attribute => Finding.OfFile(
                ViewRule.TypedRegionReferences,
                view.Path,
                XamlFile.LineOf(attribute),
                $"{attribute.Name.LocalName}=\"{attribute.Value}\" names a region with a string; refer to its typed name, such as {{x:Static regions:ShellRegions.Sidebar}}"))));

    private static bool HasDesignDataContext(XElement root) =>
        root.Elements().Any(element => element.Name.LocalName == "Design.DataContext")
        || root.Attribute(XName.Get("DataContext", XamlFile.DesignNamespace)) is not null
        || root.Attribute("Design.DataContext") is not null;

    private static IEnumerable<(XObject Node, string Property, string Value)> StyledValues(XElement element)
    {
        foreach (var attribute in element.Attributes().Where(attribute =>
            attribute.Name.Namespace == XNamespace.None && StyledProperties.Contains(LastSegment(attribute.Name.LocalName))))
        {
            yield return (attribute, attribute.Name.LocalName, attribute.Value);
        }

        if (element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value is { } property
            && StyledProperties.Contains(LastSegment(property))
            && element.Attribute("Value") is { } value)
        {
            yield return (value, property, value.Value);
        }
    }

    private static bool IsThemed(string property, string value) =>
        value.Length == 0 || value.StartsWith('{') || value == "Transparent" || (LastSegment(property) == "CornerRadius" && value == "0");

    private static bool IsIconOnly(XElement button) =>
        button.Attribute("Content") is null
        && !button.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
        && !button.Elements().Where(child => !child.Name.LocalName.Contains('.', StringComparison.Ordinal))
            .DescendantsAndSelf()
            .Any(element => TextElements.Contains(element.Name.LocalName) || element.Name.LocalName == "ContentControl");

    private static string LastSegment(string name) => name[(name.LastIndexOf('.') + 1)..];
}
