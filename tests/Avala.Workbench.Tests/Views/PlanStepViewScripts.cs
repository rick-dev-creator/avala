using Avala.Testing.UI;
using Avala.Workbench.Conversation;
using Avala.Workbench.Review;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.VisualTree;
using Path = Avalonia.Controls.Shapes.Path;

namespace Avala.Workbench.Tests.Views;

public sealed class PlanStepViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheInlinePlanThePlanPanelAndTheReviewDrawAStepWithTheSameGlyphForItsStatusAsync() =>
        ui.RunAsync(() =>
        {
            var plan = Glyphs(Screen.Show(new DesignPlanViewModel()));
            var panel = Glyphs(Screen.Show(new DesignPlanPanelViewModel { IsExpanded = true }));
            var review = Glyphs(Screen.Show(new DesignReviewViewModel()));

            Assert.Equal(["check", "check", "dot", "ring"], plan);
            Assert.Equal(plan, panel);
            Assert.Equal(["dot"], review);
        }, TestContext.Current.CancellationToken);

    private static IReadOnlyList<string> Glyphs(ViewScript view) =>
        [.. view.All<Grid>().Where(step => step.Classes.Contains("step") && step.IsEffectivelyVisible)
            .Select(step => string.Join("+", step.GetVisualDescendants().OfType<Shape>().Where(shape => shape.IsEffectivelyVisible).Select(shape => Glyph(view, shape))))];

    private static string Glyph(ViewScript view, Shape shape) => shape switch
    {
        Path path when ReferenceEquals(path.Data, view.Window.FindResource("IconCheck")) => "check",
        Ellipse { Fill: not null } => "dot",
        Ellipse => "ring",
        Arc => "spinner",
        _ => shape.GetType().Name,
    };
}
