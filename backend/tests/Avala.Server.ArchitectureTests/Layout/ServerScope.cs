using System.Reflection;
using Architecture = ArchUnitNET.Domain.Architecture;
using ArchLoader = ArchUnitNET.Loader.ArchLoader;

namespace Avala.Server.ArchitectureTests.Layout;

internal sealed class ServerScope
{
    private readonly Lazy<Architecture> architecture;

    public ServerScope(string sourceDirectory)
    {
        SourceDirectory = sourceDirectory;
        ProjectNames = BackendLayout.ProjectNamesUnder(sourceDirectory);
        Assemblies =
        [
            .. ProjectNames
                .Select(name => ServerNames.TryParse(name, out var module, out var layer)
                    ? new ModuleAssembly(module, layer, Assembly.Load(new AssemblyName(name)))
                    : null)
                .OfType<ModuleAssembly>(),
        ];
        architecture = new(() => new ArchLoader().LoadAssemblies([.. Assemblies.Select(found => found.Assembly)]).Build());
    }

    public string SourceDirectory { get; }

    public IReadOnlyList<string> ProjectNames { get; }

    public IReadOnlyList<ModuleAssembly> Assemblies { get; }

    public Architecture Architecture => architecture.Value;

    public IEnumerable<ModuleAssembly> In(Layer layer) => Assemblies.Where(found => found.Layer == layer);

    public IEnumerable<(ModuleAssembly Owner, Type Type)> Types =>
        Assemblies.SelectMany(owner => owner.Types.Select(type => (owner, type)));

    public ModuleAssembly? Owning(Type type) =>
        Assemblies.FirstOrDefault(found => found.Assembly == type.Assembly);

    public ModuleAssembly? Owning(string assemblyFullName) =>
        Assemblies.FirstOrDefault(found => found.Assembly.FullName == assemblyFullName);
}
