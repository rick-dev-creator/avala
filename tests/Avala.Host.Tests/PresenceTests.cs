using Avala.Host.Composition;
using Avala.Runtime.Presence;
using Avala.Shell;
using Avala.Testing.UI;
using Avalonia.Input;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Host.Tests;

public sealed class PresenceTests(HeadlessUi ui)
{
    [Fact]
    public Task AKeyPressedInTheMainWindowMarksThePersonAsSeenAsync() =>
        ui.RunAsync(() =>
        {
            var started = new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);
            var clock = new FakeTimeProvider(started);
            var presence = new WindowPresence(clock);
            var view = ViewScript.Present(new ShellView(), new DesignShellViewModel());
            PresenceWatch.Follow(view.Window, presence);
            clock.Advance(TimeSpan.FromMinutes(5));
            var before = presence.LastSeen;

            view.Press(Key.Escape);

            Assert.Equal((started, started.AddMinutes(5)), (before, presence.LastSeen));
        }, TestContext.Current.CancellationToken);
}
