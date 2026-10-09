using Avala.ArchitectureTests.Source;

namespace Avala.ArchitectureTests.SelfTests;

public sealed class CodeBehindInspectorTests
{
    [Fact]
    public void AcceptsAnInitializingConstructor()
    {
        const string source = """
            using Avalonia.Controls;

            namespace Sample;

            internal sealed partial class WidgetView : UserControl
            {
                public WidgetView() => InitializeComponent();
            }
            """;

        Assert.Empty(CodeBehindInspector.FindViolations(source));
    }

    [Fact]
    public void AcceptsPresentationConcerns()
    {
        const string source = """
            using Avalonia.Controls;
            using Avalonia.Interactivity;

            namespace Sample;

            internal sealed partial class WidgetView : UserControl
            {
                public WidgetView() => InitializeComponent();

                protected override void OnLoaded(RoutedEventArgs e)
                {
                    base.OnLoaded(e);
                    BringIntoView();
                }
            }
            """;

        Assert.Empty(CodeBehindInspector.FindViolations(source));
    }

    [Fact]
    public void RejectsConstructorDependencies()
    {
        const string source = """
            using Avalonia.Controls;

            namespace Sample;

            internal sealed partial class WidgetView : UserControl
            {
                public WidgetView(object service) => InitializeComponent();
            }
            """;

        Assert.Single(CodeBehindInspector.FindViolations(source));
    }

    [Fact]
    public void RejectsServicesModuleContractsAndViewModels()
    {
        const string source = """
            using Avala.Jobs.Contracts;
            using Avalonia.Controls;

            namespace Sample;

            internal sealed partial class WidgetView : UserControl
            {
                public WidgetView() => InitializeComponent();

                private void Approve() => ((WidgetViewModel)DataContext!).Approve();
            }
            """;

        Assert.Equal(2, CodeBehindInspector.FindViolations(source).Count);
    }
}
