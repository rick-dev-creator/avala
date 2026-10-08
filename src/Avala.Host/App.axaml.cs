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
            var composition = CompositionRoot.Create(PluginDirectory.Resolve());
            composition.Start();
            desktop.Exit += (_, _) => _ = composition.DisposeAsync().AsTask();
            DataTemplates.Add(composition.Views);
            desktop.MainWindow = new ShellView
            {
                DataContext = composition.Services.GetRequiredService<ShellViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
