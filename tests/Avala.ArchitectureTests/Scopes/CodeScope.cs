using System.Reflection;
using System.Runtime.CompilerServices;
using ArchLoader = ArchUnitNET.Loader.ArchLoader;
using Architecture = ArchUnitNET.Domain.Architecture;
using IType = ArchUnitNET.Domain.IType;

namespace Avala.ArchitectureTests.Scopes;

internal sealed class CodeScope(IReadOnlyList<Assembly> assemblies, string namespacePrefix, string sourceDirectory)
{
    private readonly Lazy<Architecture> architecture =
        new(() => new ArchLoader().LoadAssemblies([.. assemblies]).Build());

    public string SourceDirectory { get; } = sourceDirectory;

    public IReadOnlyList<Type> Types =>
        [
            .. assemblies.SelectMany(assembly => assembly.GetTypes())
                .Where(type => Includes(type.Namespace)
                    && !type.Name.Contains('<', StringComparison.Ordinal)
                    && !type.IsDefined(typeof(CompilerGeneratedAttribute), false)),
        ];

    public IEnumerable<IType> ArchitectureTypes =>
        architecture.Value.Types.Where(type =>
            Includes(type.Namespace.FullName) && !type.Name.Contains('<', StringComparison.Ordinal));

    public bool Includes(string? ns) =>
        ns is not null && (ns == namespacePrefix || ns.StartsWith($"{namespacePrefix}.", StringComparison.Ordinal));
}
