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
            var composition = CompositionRoot.Create(PluginDirectory.Resolve(), DataDirectory.Resolve());
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
                _ = composition.DisposeAsync().AsTask();
            };
            DataTemplates.Add(composition.Views);
            shell.Activate();
            _ = ShowAsync(desktop, composition, appearance, shell);
        }

        base.OnFrameworkInitializationCompleted();
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
