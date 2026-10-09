using Avala.Components.Status;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Media;

namespace Avala.Components.UI.Tests;

public sealed class StatusPillViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ThePillShowsItsTextAndItsDotAsync() =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(new DesignStatusPillViewModel());

            Assert.Equal("Held · stalled", view.TextOf("Label"));
            Assert.True(view.Shows("Core"));
        }, Cancellation);

    [Theory]
    [InlineData("NeedsYou", "#E5A13A")]
    [InlineData("Held", "#E5A13A")]
    [InlineData("Failed", "#EF6461")]
    [InlineData("Working", "#A3A3AD")]
    [InlineData("ReadyForReview", "#A3A3AD")]
    public Task OnlyAttentionAndFailureTintThePillAsync(string state, string color) =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(new StatusPillViewModel(Enum.Parse<StatusKind>(state), state));

            Assert.Equal(Color.Parse(color), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<TextBlock>("Label").Foreground).Color);
        }, Cancellation);

    [Fact]
    public Task ARunningPillThatIsHeldTurnsAmberAndStopsPulsingAsync() =>
        ui.RunAsync(() =>
        {
            var pill = new StatusPillViewModel(StatusKind.Working, "Running");
            var view = ViewScript.Show(pill);
            Assert.True(view.Shows("Pulse"));

            pill.Kind = StatusKind.Held;
            pill.Text = "Held · stalled";
            view.Settle();

            Assert.Equal("Held · stalled", view.TextOf("Label"));
            Assert.False(view.Shows("Pulse"));
            Assert.Equal(Color.Parse("#E5A13A"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<TextBlock>("Label").Foreground).Color);
        }, Cancellation);
}
