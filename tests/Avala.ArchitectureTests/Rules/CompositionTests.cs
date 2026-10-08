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

    [Fact]
    public void OnlyAvaloniaTypesMayBeInherited()
    {
        var violations = Classes
            .Where(type => type.BaseType is { } baseType
                && baseType != typeof(object)
                && baseType.Assembly.GetName().Name?.StartsWith("Avalonia", StringComparison.Ordinal) != true)
            .Select(type => $"{type.FullName} : {type.BaseType}");

        Assert.Empty(violations);
    }
}
