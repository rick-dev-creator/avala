using System.Reflection;
using System.Runtime.CompilerServices;

namespace Avala.Server.ArchitectureTests.Layout;

internal sealed record ModuleAssembly(string Module, Layer Layer, Assembly Assembly)
{
    public string Name => ServerNames.Project(Module, Layer);

    public IEnumerable<Type> Types =>
        Assembly.GetTypes().Where(IsWritten);

    private static bool IsWritten(Type type) =>
        !type.Name.Contains('<', StringComparison.Ordinal)
        && !type.IsDefined(typeof(CompilerGeneratedAttribute), false)
        && (type.DeclaringType is null || IsWritten(type.DeclaringType));

    public bool IsOwn(string assemblyName, params Layer[] layers) =>
        layers.Any(layer => assemblyName == ServerNames.Project(Module, layer));
}
