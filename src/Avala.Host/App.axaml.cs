using Avala.Host.Composition;
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
            composition.Start();
            var shell = composition.Services.GetRequiredService<ShellViewModel>();
            desktop.Exit += (_, _) =>
            {
                shell.Deactivate();
                _ = composition.DisposeAsync().AsTask();
            };
            DataTemplates.Add(composition.Views);
            shell.Activate();
            desktop.MainWindow = new ShellView { DataContext = shell };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
