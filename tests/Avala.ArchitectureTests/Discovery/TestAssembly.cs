using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Avala.ArchitectureTests.Solution;

namespace Avala.ArchitectureTests.Discovery;

internal sealed record TestAssembly(string Name, string Path)
{
    private static readonly string OutputFolder =
        System.IO.Path.GetRelativePath(System.IO.Path.Combine(SolutionLayout.TestsDirectory, "Avala.ArchitectureTests"), AppContext.BaseDirectory);

    public static IReadOnlyList<TestAssembly> All { get; } =
    [
        .. Directory.EnumerateDirectories(SolutionLayout.TestsDirectory)
            .Select(System.IO.Path.GetFileName)
            .OfType<string>()
            .Where(name => name.EndsWith("Tests", StringComparison.Ordinal)
                && File.Exists(System.IO.Path.Combine(SolutionLayout.TestsDirectory, name, $"{name}.csproj")))
            .Order(StringComparer.Ordinal)
            .Select(name => new TestAssembly(name, System.IO.Path.Combine(SolutionLayout.TestsDirectory, name, OutputFolder, $"{name}.dll"))),
    ];

    public bool Ships(string assembly) =>
        File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path) ?? ".", $"{assembly}.dll"));

    public async Task<IReadOnlyList<string>> UnshippedAttributeTypesAsync(CancellationToken cancellationToken)
    {
        using var image = new PEReader(ImmutableArray.Create(await File.ReadAllBytesAsync(Path, cancellationToken)));
        var metadata = image.GetMetadataReader();

        return
        [
            .. metadata.CustomAttributes
                .Select(metadata.GetCustomAttribute)
                .SelectMany(attribute => Named(attribute).Select(type => (Owner: Owner(metadata, attribute.Parent), Type: type)))
                .Where(named => named.Type.Assembly.StartsWith("Avala.", StringComparison.Ordinal) && !Ships(named.Type.Assembly))
                .Select(named => $"{Name}: {named.Owner} names {named.Type.FullName} from {named.Type.Assembly}, which the test project does not ship")
                .Distinct(),
        ];
    }

    private static IReadOnlyList<NamedType> Named(CustomAttribute attribute)
    {
        var types = new AttributeTypes();
        _ = attribute.DecodeValue(types);

        return types.Named;
    }

    private static string Owner(MetadataReader metadata, EntityHandle parent) => parent.Kind switch
    {
        HandleKind.MethodDefinition => Method(metadata, metadata.GetMethodDefinition((MethodDefinitionHandle)parent)),
        HandleKind.TypeDefinition => Type(metadata, metadata.GetTypeDefinition((TypeDefinitionHandle)parent)),
        _ => "the assembly",
    };

    private static string Method(MetadataReader metadata, MethodDefinition method) =>
        $"{Type(metadata, metadata.GetTypeDefinition(method.GetDeclaringType()))}.{metadata.GetString(method.Name)}";

    private static string Type(MetadataReader metadata, TypeDefinition type) =>
        $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}";
}
