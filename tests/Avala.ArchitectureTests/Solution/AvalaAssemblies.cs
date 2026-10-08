using System.Reflection;
using System.Runtime.CompilerServices;

namespace Avala.ArchitectureTests.Solution;

internal static class AvalaAssemblies
{
    public static IReadOnlyList<Assembly> All { get; } =
        [.. SolutionLayout.SourceProjects.Select(project => Load(project.Name))];

    public static IEnumerable<Type> AllTypes => All.SelectMany(assembly => assembly.AvalaTypes);

    public static Assembly Load(string name) => Assembly.Load(new AssemblyName(name));

    public static bool IsAvala(string name) =>
        name == "Avala" || name.StartsWith("Avala.", StringComparison.Ordinal);

    extension(Assembly assembly)
    {
        public IReadOnlyList<string> ReferencedAvalaAssemblies =>
            [.. assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty).Where(IsAvala)];

        public bool ReferencesAvalonia =>
            assembly.GetReferencedAssemblies()
                .Any(reference => reference.Name?.StartsWith("Avalonia", StringComparison.Ordinal) == true);

        public IReadOnlyList<Type> ExportedAvalaTypes =>
            [.. assembly.GetExportedTypes().Where(type => type.Namespace is { } ns && IsAvala(ns))];

        public IEnumerable<Type> AvalaTypes =>
            assembly.GetTypes().Where(type =>
                type.Namespace is { } ns
                && IsAvala(ns)
                && !type.Name.Contains('<', StringComparison.Ordinal)
                && !type.IsDefined(typeof(CompilerGeneratedAttribute), false));
    }
}
