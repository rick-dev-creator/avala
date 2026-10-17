using System.Globalization;
using Avala.Documentation.Contracts;

namespace Avala.Workbench.Docs;

internal static class DocsPhrases
{
    public static string Kind(C4Element element) => (element.Shape, element.External) switch
    {
        (C4Shape.Person, _) => "Person",
        (_, true) => "External system",
        (C4Shape.System, _) => "System",
        (C4Shape.Database, _) => "Database",
        (C4Shape.Queue, _) => "Queue",
        (C4Shape.Component, _) => "Component",
        _ => "Container",
    };

    public static string Mark(StepKind kind) => kind switch
    {
        StepKind.Added => "new",
        StepKind.Changed => "changed",
        StepKind.Removed => "removed",
        StepKind.Flow => "flow",
        _ => string.Empty,
    };

    public static string Version(PlayedVersion version) =>
        string.Create(CultureInfo.InvariantCulture, $"v{version.Number} {Label(version.Kind)}");

    public static string Label(VersionKind kind) => kind switch
    {
        VersionKind.Proposed => "Proposed",
        VersionKind.Deviation => "Deviation",
        _ => "As built",
    };

    public static string Step(int step, int count, string title) =>
        step < 0 ? string.Create(CultureInfo.InvariantCulture, $"{count} steps") : string.Create(CultureInfo.InvariantCulture, $"Step {step + 1} of {count} · {title}");

    public static string Speed(double speed) => string.Create(CultureInfo.InvariantCulture, $"{speed}×");
}
