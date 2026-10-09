using Avala.Host.Composition;
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
        var composition = CompositionRoot.Create(PluginDirectory.Resolve(), paths);
        var appearance = new AppearanceApplier(this);
        _ = appearance.FollowAsync(
            composition.Services.GetRequiredService<IEventFeed>().SubscribeAsync<AppearanceChanged>(composition.Lifetime),
            composition.Services.GetRequiredService<IUiDispatcher>(),
            composition.Lifetime);
        composition.Start();
        var shell = composition.Services.GetRequiredService<ShellViewModel>();
        desktop.Exit += (_, _) =>
        {
            shell.Deactivate();
            _ = StopAsync(composition, claim);
        };
        DataTemplates.Add(composition.Views);
        shell.Activate();
        _ = ShowAsync(desktop, composition, appearance, shell);
    }

    private static async Task StopAsync(CompositionRoot composition, DataFolderClaim claim)
    {
        try
        {
            await composition.DisposeAsync();
        }
        finally
        {
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
