using System.Collections.Immutable;
using Avala.Preview.Contracts;
using Avala.Sdk;
using Avala.Sdk.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal sealed class SurfaceGate : IPlugin, IViewContributor, IWebSurfaces
{
    private ImmutableList<PaintedSurface> opened = [];

    public PluginInfo Info { get; } = new("surface-gate", "Surface gate");

    public IReadOnlyList<PaintedSurface> Opened => Volatile.Read(ref opened);

    public void Register(IPluginRegistrar registrar) => registrar.Services.AddSingleton<IWebSurfaces>(this);

    public void RegisterViews(IViewRegistrar views) => views.Register(new FuncDataTemplate<PaintedSurface>((_, _) => Page()));

    public IWebSurface Open(SurfaceRules rules)
    {
        var surface = new PaintedSurface(rules);
        ImmutableInterlocked.Update(ref opened, list => list.Add(surface));

        return surface;
    }

    private static Border Page()
    {
        var rows = new StackPanel { Spacing = 0 };

        foreach (var (order, price) in new[] { ("#1042 · Coffee beans", "€ 18.00"), ("#1043 · Grinder", "€ 89.00"), ("#1044 · Filters", "€ 4.50") })
        {
            rows.Children.Add(new DockPanel
            {
                Margin = new Thickness(0, 8),
                Children =
                {
                    new TextBlock { Text = price, Foreground = Ink, [DockPanel.DockProperty] = Dock.Right },
                    new TextBlock { Text = order, Foreground = Ink },
                },
            });
        }

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#f6f5f2")),
            Child = new StackPanel
            {
                Margin = new Thickness(32, 48),
                MaxWidth = 520,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = "Orders service", FontSize = 22, FontWeight = FontWeight.SemiBold, Foreground = Ink },
                    new TextBlock { Text = "Running on its leased port, served by the simulated job.", Foreground = new SolidColorBrush(Color.Parse("#6b6862")), Margin = new Thickness(0, 0, 0, 18) },
                    new Border
                    {
                        Background = Brushes.White,
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(20, 8),
                        BoxShadow = BoxShadows.Parse("0 1 3 0 #14000000"),
                        Child = rows,
                    },
                },
            },
        };
    }

    private static SolidColorBrush Ink => new(Color.Parse("#1d1c1a"));
}

internal sealed class PaintedSurface(SurfaceRules rules) : IWebSurface
{
    private ImmutableList<Uri> visited = [];

    public event EventHandler<Uri>? Refused;

    public event EventHandler? Changed;

    public SurfaceEmbedding Embedding => SurfaceEmbedding.Embedded;

    public IReadOnlyList<Uri> Visited => Volatile.Read(ref visited);

    public Option<Uri> Address => Visited.Count > 0 ? Visited[^1] : Option<Uri>.None;

    public bool CanGoBack => Visited.Count > 1;

    public bool IsClosed { get; private set; }

    public void Navigate(Uri address)
    {
        if (!rules.Admits(address))
        {
            Refused?.Invoke(this, address);

            return;
        }

        ImmutableInterlocked.Update(ref visited, list => list.Add(address));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reload()
    {
    }

    public void GoBack()
    {
        ImmutableInterlocked.Update(ref visited, list => list.RemoveAt(list.Count - 1));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Close() => IsClosed = true;
}
