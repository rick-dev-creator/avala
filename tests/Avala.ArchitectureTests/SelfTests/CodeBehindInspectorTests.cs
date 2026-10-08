using Avala.ArchitectureTests.Source;

namespace Avala.ArchitectureTests.SelfTests;

public sealed class CodeBehindInspectorTests
{
    [Fact]
    public void AcceptsAnExpressionBodiedInitializingConstructor()
    {
        const string source = """
            namespace Sample;

            internal sealed partial class WidgetView
            {
                public WidgetView() => InitializeComponent();
            }
            """;

        Assert.Empty(CodeBehindInspector.FindLogic(source));
    }

    [Fact]
    public void AcceptsABlockInitializingConstructor()
    {
        const string source = """
            namespace Sample;

            internal sealed partial class WidgetView
            {
                public WidgetView()
                {
                    InitializeComponent();
                }
            }
            """;

        Assert.Empty(CodeBehindInspector.FindLogic(source));
    }

    [Fact]
    public void RejectsEventHandlers()
    {
        const string source = """
            namespace Sample;

            internal sealed partial class WidgetView
            {
                public WidgetView() => InitializeComponent();

                private void OnClick(object sender, object args) => Width = 10;
            }
            """;

        Assert.Single(CodeBehindInspector.FindLogic(source));
    }

    [Fact]
    public void RejectsConstructorsThatDoMoreThanInitialize()
    {
        const string source = """
            namespace Sample;

            internal sealed partial class WidgetView
            {
                public WidgetView()
                {
                    InitializeComponent();
                    DataContext = new object();
                }
            }
            """;

        Assert.Single(CodeBehindInspector.FindLogic(source));
    }
}
