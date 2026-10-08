using System.Reflection;
using Avala.ArchitectureTests.Solution;

namespace Avala.ArchitectureTests.Scopes;

internal static class CodeScopes
{
    private const string FixturesProject = "Avala.ArchitectureTests.Fixtures";

    private static readonly Assembly Fixtures = AvalaAssemblies.Load(FixturesProject);

    private static readonly string FixturesDirectory = Path.Combine(SolutionLayout.TestsDirectory, FixturesProject);

    private static readonly CodeScope Production = new(
        [.. SolutionLayout.ModuleProjects.Select(project => AvalaAssemblies.Load(project.Name))],
        "Avala",
        Path.Combine(SolutionLayout.SourceDirectory, "Modules"));

    private static readonly CodeScope Compliant = new(
        [Fixtures],
        "Avala.Fixtures.Compliant",
        Path.Combine(FixturesDirectory, "Compliant"));

    private static readonly CodeScope Violating = new(
        [Fixtures],
        "Avala.Fixtures.Violating",
        Path.Combine(FixturesDirectory, "Violating"));

    public static CodeScope Of(Scope scope) => scope switch
    {
        Scope.Production => Production,
        Scope.Compliant => Compliant,
        Scope.Violating => Violating,
        _ => throw new ArgumentOutOfRangeException(nameof(scope)),
    };
}
