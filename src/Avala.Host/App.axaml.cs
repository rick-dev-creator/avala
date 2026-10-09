using Avala.Host.Composition;
using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avala.Sdk.Events;
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
            var window = new ShellView { DataContext = shell };
            appearance.Attach(window);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
