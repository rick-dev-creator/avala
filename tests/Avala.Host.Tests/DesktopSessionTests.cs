using System.Reflection;
using Avala.Host.Composition;
using Avala.Testing.UI;
using Avalonia.Threading;

namespace Avala.Host.Tests;

public sealed class DesktopSessionTests(HeadlessUi ui)
{
    [Fact]
    public void OnlyALinuxWaylandSessionNotForcedToX11DrawsThroughWayland()
    {
        Assert.True(DesktopSession.DrawsThroughWayland(linux: true, waylandDisplay: "wayland-1", forcedX11: ""));
        Assert.False(DesktopSession.DrawsThroughWayland(linux: true, waylandDisplay: "", forcedX11: ""));
        Assert.False(DesktopSession.DrawsThroughWayland(linux: true, waylandDisplay: "wayland-1", forcedX11: "1"));
        Assert.False(DesktopSession.DrawsThroughWayland(linux: false, waylandDisplay: "wayland-1", forcedX11: ""));
    }

    [Theory]
    [InlineData("", "avala")]
    [InlineData("io.github.rick_dev_creator.Avala", "io.github.rick_dev_creator.Avala")]
    public void TheWindowIsNamedAfterItsDesktopEntrySoTheDesktopShowsItsIcon(string flatpakId, string appId) =>
        Assert.Equal(appId, DesktopSession.AppIdOf(flatpakId));

    [Fact]
    public async Task TheDispatcherClockFollowsThePlatformsClockAsync()
    {
        var aligned = false;
        var (before, followed, after) = (0L, long.MaxValue, 0L);

        await ui.RunAsync(
            () =>
            {
                var dispatcher = Dispatcher.UIThread;
                var fields = typeof(Dispatcher).GetRuntimeFields().Where(field => !field.IsStatic).ToList();
                var platform = fields.First(field => field.Name == "_impl").GetValue(dispatcher)!;
                var now = platform.GetType().GetInterfaces().Select(contract => contract.GetProperty("Now")).First(property => property is not null)!;
                var provider = fields.First(field => field.Name == "_timeProvider");
                provider.SetValue(dispatcher, new Func<long>(() => (long)now.GetValue(platform)! + 100_000));

                aligned = DispatcherClock.Align();
                before = (long)now.GetValue(platform)!;
                followed = ((Func<long>)provider.GetValue(dispatcher)!)();
                after = (long)now.GetValue(platform)!;
            },
            TestContext.Current.CancellationToken);

        Assert.True(aligned);
        Assert.InRange(followed, before, after);
    }
}
