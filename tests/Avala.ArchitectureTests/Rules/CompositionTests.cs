using Avala.ArchitectureTests.Solution;

namespace Avala.ArchitectureTests.Rules;

public sealed class CompositionTests
{
    private static IEnumerable<Type> Classes => AvalaAssemblies.AllTypes.Where(type => type.IsClass);

    [Fact]
    public void EveryClassIsSealed()
    {
        var violations = Classes.Where(type => !type.IsSealed).Select(type => type.FullName);

        Assert.Empty(violations);
    }

    private static readonly Type[] FrameworkBaseTypes =
    [
        typeof(object),
        typeof(Microsoft.EntityFrameworkCore.DbContext),
        typeof(Microsoft.EntityFrameworkCore.Migrations.Migration),
        typeof(Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot),
        typeof(System.Text.Json.Serialization.JsonConverterFactory),
    ];

    [Fact]
    public void OnlyFrameworkBaseTypesMayBeInherited()
    {
        var violations = Classes
            .Where(type => type.BaseType is { } baseType
                && !FrameworkBaseTypes.Contains(baseType)
                && !IsJsonConverter(baseType)
                && baseType.Assembly.GetName().Name?.StartsWith("Avalonia", StringComparison.Ordinal) != true)
            .Select(type => $"{type.FullName} : {type.BaseType}");

        Assert.Empty(violations);
    }

    private static bool IsJsonConverter(Type baseType) =>
        baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(System.Text.Json.Serialization.JsonConverter<>);
}
