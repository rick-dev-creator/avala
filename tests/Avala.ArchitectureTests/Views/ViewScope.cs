using System.Reflection;
using Avala.ArchitectureTests.Scopes;
using Avala.ArchitectureTests.Solution;
using ArchLoader = ArchUnitNET.Loader.ArchLoader;
using Architecture = ArchUnitNET.Domain.Architecture;
using IType = ArchUnitNET.Domain.IType;

namespace Avala.ArchitectureTests.Views;

internal sealed class ViewScope
{
    private const string FixturesProject = "Avala.ArchitectureTests.Fixtures";
    private const string FixturesUiProject = "Avala.ArchitectureTests.Fixtures.UI";

    private static readonly Lazy<ViewScope> Production =
        new(() => new(AvalaAssemblies.All, "Avala", [SolutionLayout.SourceDirectory], [SolutionLayout.TestsDirectory]));

    private static readonly Lazy<ViewScope> Compliant = new(() => Fixture("Compliant"));

    private static readonly Lazy<ViewScope> Violating = new(() => Fixture("Violating"));

    private readonly IReadOnlyList<Assembly> assemblies;
    private readonly string namespacePrefix;
    private readonly Lazy<Architecture> architecture;

    private ViewScope(IReadOnlyList<Assembly> assemblies, string namespacePrefix, IReadOnlyList<string> sourceDirectories, IReadOnlyList<string> scriptDirectories)
    {
        this.assemblies = assemblies;
        this.namespacePrefix = namespacePrefix;
        SourceDirectories = sourceDirectories;
        ScriptDirectories = scriptDirectories;
        architecture = new(() => new ArchLoader().LoadAssemblies([.. assemblies]).Build());
    }

    public IReadOnlyList<string> SourceDirectories { get; }

    public IReadOnlyList<string> ScriptDirectories { get; }

    public IEnumerable<Type> Types => assemblies.SelectMany(assembly => assembly.AvalaTypes).Where(type => Includes(type.Namespace));

    public IEnumerable<IType> ArchitectureTypes =>
        architecture.Value.Types.Where(type => Includes(type.Namespace.FullName) && !type.Name.Contains('<', StringComparison.Ordinal));

    public static ViewScope Of(Scope scope) => scope switch
    {
        Scope.Production => Production.Value,
        Scope.Compliant => Compliant.Value,
        Scope.Violating => Violating.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(scope)),
    };

    public IEnumerable<string> FilesUnderSources(string pattern) =>
        SourceDirectories.SelectMany(directory => SolutionLayout.FilesUnder(directory, pattern));

    private bool Includes(string? ns) =>
        ns is not null && (ns == namespacePrefix || ns.StartsWith($"{namespacePrefix}.", StringComparison.Ordinal));

    private static ViewScope Fixture(string name)
    {
        string[] directories =
        [
            Path.Combine(SolutionLayout.TestsDirectory, FixturesProject, name),
            Path.Combine(SolutionLayout.TestsDirectory, FixturesUiProject, name),
        ];

        return new(
            [AvalaAssemblies.Load(FixturesProject), AvalaAssemblies.Load(FixturesUiProject)],
            $"Avala.Fixtures.{name}",
            directories,
            directories);
    }
}
