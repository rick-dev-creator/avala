using System.Reflection.Metadata;

namespace Avala.ArchitectureTests.Discovery;

internal sealed record NamedType(string FullName, string Assembly);

internal sealed class AttributeTypes : ICustomAttributeTypeProvider<NamedType>
{
    private static readonly NamedType Framework = new(string.Empty, string.Empty);

    private readonly List<NamedType> named = [];

    public IReadOnlyList<NamedType> Named => named;

    public NamedType GetPrimitiveType(PrimitiveTypeCode typeCode) => Framework;

    public NamedType GetSystemType() => new("System.Type", string.Empty);

    public bool IsSystemType(NamedType type) => type.FullName == "System.Type";

    public NamedType GetSZArrayType(NamedType elementType) => elementType;

    public NamedType GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeDefinition(handle);

        return Seen(new NamedType($"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}", reader.GetString(reader.GetAssemblyDefinition().Name)));
    }

    public NamedType GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        var name = $"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}";

        return Seen(type.ResolutionScope.Kind switch
        {
            HandleKind.AssemblyReference => new NamedType(name, reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name)),
            HandleKind.TypeReference => GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind) with { FullName = name },
            _ => new NamedType(name, string.Empty),
        });
    }

    public NamedType GetTypeFromSerializedName(string name)
    {
        var separator = name.IndexOf(',', StringComparison.Ordinal);

        return Seen(separator < 0
            ? new NamedType(name, string.Empty)
            : new NamedType(name[..separator].Trim(), name[(separator + 1)..].Split(',')[0].Trim()));
    }

    public PrimitiveTypeCode GetUnderlyingEnumType(NamedType type) =>
        Type.GetType($"{type.FullName}, {type.Assembly}")?.GetEnumUnderlyingType() switch
        {
            { } underlying when underlying == typeof(byte) => PrimitiveTypeCode.Byte,
            { } underlying when underlying == typeof(sbyte) => PrimitiveTypeCode.SByte,
            { } underlying when underlying == typeof(short) => PrimitiveTypeCode.Int16,
            { } underlying when underlying == typeof(ushort) => PrimitiveTypeCode.UInt16,
            { } underlying when underlying == typeof(uint) => PrimitiveTypeCode.UInt32,
            { } underlying when underlying == typeof(long) => PrimitiveTypeCode.Int64,
            { } underlying when underlying == typeof(ulong) => PrimitiveTypeCode.UInt64,
            _ => PrimitiveTypeCode.Int32,
        };

    private NamedType Seen(NamedType type)
    {
        named.Add(type);

        return type;
    }
}
