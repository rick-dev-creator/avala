using System.Runtime.InteropServices;
using Avala.Host.Composition;
using Avala.Runtime.Diagnostics;
using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;
using Avala.Shell;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host;

internal sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = DataDirectory.Resolve();
            _ = DataFolderClaim.TryTake(paths).Match(
                claim =>
                {
                    Run(desktop, paths, claim);
                    return true;
                },
                () =>
                {
                    Refuse(desktop, paths);
                    return false;
                });
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void Refuse(IClassicDesktopStyleApplicationLifetime desktop, AvalaPaths paths)
    {
        var refusal = new DataFolderInUseViewModel(paths.Data);
        refusal.QuitRequested += (_, _) => desktop.Shutdown(1);
        desktop.MainWindow = new DataFolderInUseView { DataContext = refusal };
    }

    private void Run(IClassicDesktopStyleApplicationLifetime desktop, AvalaPaths paths, DataFolderClaim claim)
    {
        var log = new LogFile(paths, TimeProvider.System);
        var crashes = new CrashLog(log);
        crashes.Watch();
        crashes.Started($"Avala {CompositionRoot.Build.Version} ({CompositionRoot.Build.Commit.Match(commit => commit, () => AvalaBuild.Unknown)}) started on {RuntimeInformation.OSDescription}, data folder {paths.Data}");
        var smoke = SmokeRun.IsRequested(desktop.Args ?? []);
        var composition = Composed(paths, log, crashes, smoke ? Option<HttpMessageHandler>.None : new SocketsHttpHandler());
        var appearance = new AppearanceApplier(this);
        var feed = composition.Services.GetRequiredService<IEventFeed>();
        _ = appearance.FollowAsync(
            feed.SubscribeAsync<AppearanceChanged>(composition.Lifetime),
            composition.Services.GetRequiredService<IUiDispatcher>(),
            composition.Lifetime);
        var started = feed.SubscribeAsync<StartupCompleted>(composition.Lifetime);
        composition.Start();
        _ = crashes.ObserveAsync(composition.Running);
        var shell = composition.Services.GetRequiredService<ShellViewModel>();
        desktop.Exit += (_, _) =>
        {
            shell.Deactivate();
            _ = StopAsync(composition, claim, log, crashes);
        };
        DataTemplates.Add(composition.Views);
        shell.Activate();
        var showing = ShowAsync(desktop, composition, appearance, shell);
        _ = smoke ? SmokeRun.PassAsync(desktop, showing, started, shell) : showing;
    }

    private static CompositionRoot Composed(AvalaPaths paths, LogFile log, CrashLog crashes, Option<HttpMessageHandler> releases)
    {
        try
        {
            return CompositionRoot.Create(PluginDirectory.Resolve(), paths, log, releases);
        }
        catch (Exception failure)
        {
            crashes.Failed(failure);
            throw;
        }
    }

    private static async Task StopAsync(CompositionRoot composition, DataFolderClaim claim, LogFile log, CrashLog crashes)
    {
        try
        {
            await composition.DisposeAsync();
        }
        finally
        {
            crashes.Unwatch();
            await log.DisposeAsync();
            claim.Dispose();
        }
    }

    private static async Task ShowAsync(IClassicDesktopStyleApplicationLifetime desktop, CompositionRoot composition, AppearanceApplier appearance, ShellViewModel shell)
    {
        var startup = new AppearanceStartup(
            composition.Services.GetRequiredService<IAppearance>(),
            new SystemMotion(composition.Services.GetRequiredService<IProcessRunner>()),
            appearance);

        desktop.MainWindow = await startup.ShowAsync(() => new ShellView { DataContext = shell }, composition.Lifetime);
    }
}
