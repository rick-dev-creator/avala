using Avala.Components.Status;
using Avala.Testing.UI;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Avala.Components.UI.Tests;

public sealed class StatusDotViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AWorkingDotPulsesAndShowsNoSpinnerAsync() =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(new DesignStatusDotViewModel());

            Assert.True(view.Shows("Pulse"));
            Assert.True(view.HasClass("Pulse", "pulse"));
            Assert.False(view.Shows("Spinner"));
        }, Cancellation);

    [Fact]
    public Task TheWorkingPulseSpreadsBeyondTheDotUnclippedAsync() =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(new StatusPillViewModel(StatusKind.Working, "Running"));

            Assert.All(
                view.Find("Pulse").GetVisualAncestors().OfType<Avalonia.Visual>().TakeWhile(ancestor => ancestor is not Avalonia.Controls.Window && ancestor.GetType().Name != "StatusPillView"),
                ancestor => Assert.False(ancestor.ClipToBounds, ancestor.GetType().Name));
        }, Cancellation);

    [Fact]
    public Task ADotBeingCheckedSpinsInsteadOfShowingItsCoreAsync() =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(new StatusDotViewModel(StatusKind.Checking));

            Assert.True(view.Shows("Spinner"));
            Assert.False(view.Shows("Core"));
            Assert.False(view.Shows("Pulse"));
        }, Cancellation);

    [Fact]
    public Task AHeldDotStopsPulsingAndBecomesAnAmberRingAsync() =>
        ui.RunAsync(() =>
        {
            var dot = new StatusDotViewModel(StatusKind.Working);
            var view = ViewScript.Show(dot);

            dot.Kind = StatusKind.Held;
            view.Settle();

            var core = view.Find<Ellipse>("Core");
            Assert.False(view.Shows("Pulse"));
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(core.Fill).Color);
            Assert.Equal(Color.Parse("#E5A13A"), Assert.IsAssignableFrom<ISolidColorBrush>(core.Stroke).Color);
        }, Cancellation);

    [Theory]
    [InlineData("NeedsYou", "#E5A13A")]
    [InlineData("Done", "#45454D")]
    [InlineData("Failed", "#EF6461")]
    [InlineData("Working", "#EDEDEF")]
    public Task EveryFilledStateHasItsColorAsync(string state, string color) =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(new StatusDotViewModel(Enum.Parse<StatusKind>(state)));

            Assert.Equal(Color.Parse(color), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<Ellipse>("Core").Fill).Color);
        }, Cancellation);
}
