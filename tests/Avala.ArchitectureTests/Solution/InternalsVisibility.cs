using System.Reflection;
using System.Runtime.CompilerServices;

namespace Avala.ArchitectureTests.Solution;

internal static class InternalsVisibility
{
    private const string Host = "Avala.Host";

    public static IEnumerable<string> Targets(Assembly assembly) =>
        assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName.Split(',')[0].Trim());

    public static IEnumerable<string> Violations(
        SourceProject project,
        IEnumerable<string> targets,
        IEnumerable<SourceProject> projects) =>
        targets
            .Where(target => !IsAllowed(project, target, projects))
            .Select(target => $"{project.Name} -> {target}");

    private static bool IsAllowed(SourceProject project, string target, IEnumerable<SourceProject> projects) =>
        target == $"{project.Name}.Tests"
        || (project.Module is null
            ? target == Host
            : projects.Any(other => other.Module == project.Module && other.Name == target));
}
