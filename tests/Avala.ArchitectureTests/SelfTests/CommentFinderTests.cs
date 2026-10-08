using Avala.ArchitectureTests.Source;

namespace Avala.ArchitectureTests.SelfTests;

public sealed class CommentFinderTests
{
    [Fact]
    public void FindsSingleLineComments()
    {
        const string source = """
            namespace Sample;

            // explains nothing
            internal sealed class Widget;
            """;

        Assert.Equal([3], CommentFinder.InCSharp(source));
    }

    [Fact]
    public void FindsBlockComments()
    {
        const string source = """
            namespace Sample;

            internal sealed class Widget
            {
                /* hidden */ private readonly int size;
            }
            """;

        Assert.Equal([5], CommentFinder.InCSharp(source));
    }

    [Fact]
    public void FindsDocumentationComments()
    {
        const string source = """
            namespace Sample;

            /// <summary>Widget.</summary>
            internal sealed class Widget;
            """;

        Assert.Equal([3], CommentFinder.InCSharp(source));
    }

    [Fact]
    public void IgnoresCommentMarkersInsideStrings()
    {
        const string source = """
            namespace Sample;

            internal sealed class Widget
            {
                private const string Url = "https://example.com";
            }
            """;

        Assert.Empty(CommentFinder.InCSharp(source));
    }

    [Fact]
    public void FindsXamlComments()
    {
        const string source = """
            <UserControl>
              <!-- layout -->
              <Grid />
            </UserControl>
            """;

        Assert.Equal([2], CommentFinder.InXaml(source));
    }
}
