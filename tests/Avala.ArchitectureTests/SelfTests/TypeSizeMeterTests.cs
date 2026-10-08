using Avala.ArchitectureTests.Source;

namespace Avala.ArchitectureTests.SelfTests;

public sealed class TypeSizeMeterTests
{
    [Fact]
    public void CountsTheLinesOfATypeDeclaration()
    {
        const string source = """
            namespace Sample;

            internal sealed class Widget
            {
                private readonly int size;
            }
            """;

        Assert.Equal(4, TypeSizeMeter.Measure([source])["Sample.Widget"]);
    }

    [Fact]
    public void AddsUpEveryPartOfAPartialType()
    {
        const string first = """
            namespace Sample;

            internal sealed partial class Widget
            {
                private readonly int size;
            }
            """;
        const string second = """
            namespace Sample;

            internal sealed partial class Widget
            {
                private readonly int width;
                private readonly int height;
            }
            """;

        Assert.Equal(9, TypeSizeMeter.Measure([first, second])["Sample.Widget"]);
    }

    [Fact]
    public void TellsGenericTypesApartFromTheirNamesakes()
    {
        const string source = """
            namespace Sample;

            internal sealed class Box;

            internal sealed class Box<T>;
            """;

        var sizes = TypeSizeMeter.Measure([source]);

        Assert.Equal(1, sizes["Sample.Box"]);
        Assert.Equal(1, sizes["Sample.Box`1"]);
    }
}
