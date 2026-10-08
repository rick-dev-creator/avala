using Avala.ArchitectureTests.Solution;
using Avala.Sdk;

namespace Avala.ArchitectureTests.Rules;

public sealed class ModularMonolithTests
{
    private static readonly string[] HostDependencies = ["Avala.Runtime", "Avala.Sdk", "Avala.Sdk.UI", "Avala.Shell"];

    private static readonly string[] ProjectsWithLogic = ["Avala.Runtime", "Avala.Sdk", "Avala.Shell"];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HostProjectReferencesOnlyTheCoreProjectsAsync()
    {
        var references = await SolutionLayout.ProjectReferencesAsync(
            SolutionLayout.Project("Avala.Host").Path,
            Cancellation);

        Assert.Empty(references.Except(HostDependencies));
    }

    [Fact]
    public void HostAssemblyKnowsNoModule()
    {
        var modules = SolutionLayout.ModuleProjects.Select(project => project.Name);

        Assert.Empty(AvalaAssemblies.Load("Avala.Host").ReferencedAvalaAssemblies.Intersect(modules));
    }

    [Fact]
    public void ShellDependsOnlyOnTheSdk() =>
        Assert.Empty(AvalaAssemblies.Load("Avala.Shell").ReferencedAvalaAssemblies.Except(["Avala.Sdk"]));

    [Fact]
    public void SdkDependsOnNothingFromAvalaNorAvalonia()
    {
        var sdk = AvalaAssemblies.Load("Avala.Sdk");

        Assert.Empty(sdk.ReferencedAvalaAssemblies);
        Assert.False(sdk.ReferencesAvalonia);
    }

    [Fact]
    public void RuntimeDependsOnlyOnTheSdk() =>
        Assert.Empty(AvalaAssemblies.Load("Avala.Runtime").ReferencedAvalaAssemblies.Except(["Avala.Sdk"]));

    [Fact]
    public void SdkUiDependsOnlyOnTheSdk() =>
        Assert.Empty(AvalaAssemblies.Load("Avala.Sdk.UI").ReferencedAvalaAssemblies.Except(["Avala.Sdk"]));

    [Fact]
    public void ModulesDependOnlyOnTheSdkTheirOwnProjectsAndOtherContracts()
    {
        var violations = SolutionLayout.ModuleProjects
            .SelectMany(project => AvalaAssemblies.Load(project.Name).ReferencedAvalaAssemblies
                .Where(reference => !IsAllowedModuleDependency(project, reference))
                .Select(reference => $"{project.Name} -> {reference}"));

        Assert.Empty(violations);
    }

    [Fact]
    public void ContractsDependOnlyOnTheSdkAndOtherContracts()
    {
        var contracts = SolutionLayout.ModuleProjects
            .Where(project => project.Kind == ProjectKind.Contracts)
            .Select(project => project.Name)
            .ToList();

        var violations = contracts
            .SelectMany(name => AvalaAssemblies.Load(name).ReferencedAvalaAssemblies
                .Where(reference => reference != "Avala.Sdk" && !contracts.Contains(reference))
                .Select(reference => $"{name} -> {reference}"));

        Assert.Empty(violations);
    }

    [Fact]
    public void EveryModuleExposesOnlyItsPluginEntry()
    {
        var violations = SolutionLayout.ModuleProjects
            .Where(project => project.Kind != ProjectKind.Contracts)
            .GroupBy(project => project.Module)
            .Select(module => (Module: module.Key, Exported: module.SelectMany(project => AvalaAssemblies.Load(project.Name).ExportedAvalaTypes).ToList()))
            .Where(module => module.Exported is not [var single] || !typeof(IPlugin).IsAssignableFrom(single))
            .Select(module => $"{module.Module} exposes [{string.Join(", ", module.Exported.Select(type => type.Name))}]");

        Assert.Empty(violations);
    }

    [Fact]
    public void InternalsAreVisibleOnlyWithinTheirModuleAndTests()
    {
        var violations = SolutionLayout.SourceProjects.SelectMany(project =>
            InternalsVisibility.Violations(
                project,
                InternalsVisibility.Targets(AvalaAssemblies.Load(project.Name)),
                SolutionLayout.SourceProjects));

        Assert.Empty(violations);
    }

    [Fact]
    public void DetectsInternalsExposedToAnotherModule()
    {
        SourceProject[] projects =
        [
            new("Avala.Jobs", "Avala.Jobs.csproj", "Jobs"),
            new("Avala.Jobs.UI", "Avala.Jobs.UI.csproj", "Jobs"),
            new("Avala.Agents", "Avala.Agents.csproj", "Agents"),
        ];

        var violations = InternalsVisibility.Violations(
            projects[0],
            ["Avala.Jobs.UI", "Avala.Jobs.Tests", "Avala.Agents", "Avala.Host"],
            projects);

        Assert.Equal(["Avala.Jobs -> Avala.Agents", "Avala.Jobs -> Avala.Host"], violations);
    }

    [Fact]
    public async Task ArchitectureTestsCoverEverySourceProjectAsync()
    {
        var covered = await SolutionLayout.ProjectReferencesAsync(
            Path.Combine(SolutionLayout.TestsDirectory, "Avala.ArchitectureTests", "Avala.ArchitectureTests.csproj"),
            Cancellation);

        Assert.Empty(SolutionLayout.SourceProjects.Select(project => project.Name).Except(covered));
    }

    [Fact]
    public void ProjectsWithLogicHaveUnitTestProjects()
    {
        var missing = SolutionLayout.SourceProjects
            .Where(project => ProjectsWithLogic.Contains(project.Name) || project is { Module: not null, Kind: ProjectKind.Core })
            .Select(project => $"{project.Name}.Tests")
            .Where(name => !File.Exists(Path.Combine(SolutionLayout.TestsDirectory, name, $"{name}.csproj")));

        Assert.Empty(missing);
    }

    private static bool IsAllowedModuleDependency(SourceProject project, string reference) =>
        reference is "Avala.Sdk" or "Avala.Sdk.UI"
        || SolutionLayout.ModuleProjects.Any(other =>
            other.Name == reference && (other.Module == project.Module || other.Kind == ProjectKind.Contracts));
}
