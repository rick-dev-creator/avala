using System.ComponentModel;
using System.Xml.Linq;
using Avala.Host.Composition;
using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avala.Sdk.Regions;
using Avala.Shell;
using Avala.Shell.Regions;
using Avala.Testing;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

public sealed partial class ShellViewScripts(HeadlessUi ui, PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheDesignTimeShellFillsEveryRegionAsync() =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Present(new ShellView(), new DesignShellViewModel());

            Assert.True(view.Shows("SidebarPane"));
            Assert.True(view.Shows("InspectorPane"));
            Assert.True(view.Shows("EmptyContent"));
            Assert.True(view.Shows("PageNavigation"));
            Assert.True(view.HasClass("InspectorPane", "shown"));
            Assert.Superset(
                new HashSet<string>(["Avala", "⌘K", "jump", "Add invoice PDF endpoint", "Fix JPY rounding in invoice totals", "claude-work · 5h", "88% · resets 16:20", "Inspector", "claude-personal · 5h", "Nothing to show yet", "Overview", "Usage", "Settings"]),
                view.VisibleTexts.ToHashSet());
            Assert.All(view.Find<ListBox>("PageNavigation").GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>(), icon => Assert.NotNull(icon.Data));
        }, Cancellation);

    [Fact]
    public Task TheMainWindowCarriesTheBrandIconAsync() =>
        ui.RunAsync(async () =>
        {
            var view = ViewScript.Present(new ShellView(), new DesignShellViewModel());
            using var shipped = new MemoryStream();
            await using (var resource = Avalonia.Platform.AssetLoader.Open(new Uri("avares://Avala.Host/Assets/avala.ico")))
            {
                await resource.CopyToAsync(shipped, Cancellation);
            }

            Assert.NotNull(view.Window.Icon);
            Assert.Equal(await File.ReadAllBytesAsync(BrandIcon, Cancellation), shipped.ToArray());
        }, Cancellation);

    [Fact]
    public async Task TheExecutableCarriesTheBrandIconAsync()
    {
        var project = XDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Repository.Root.FullName, "src", "Avala.Host", "Avala.Host.csproj"), Cancellation));
        var declared = Assert.Single(project.Descendants("ApplicationIcon")).Value.Replace("$(AvalaRoot)", string.Empty, StringComparison.Ordinal);

        Assert.Equal(BrandIcon, Path.Combine(Repository.Root.FullName, declared.Replace('\\', Path.DirectorySeparatorChar)));
    }

    private static string BrandIcon => Path.Combine(Repository.Root.FullName, "docs", "assets", "brand", "avala.ico");

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task TheShellDrawsEachThemeVariantWithLegibleTextAsync(string name) =>
        ui.RunAsync(() =>
        {
            var variant = name == "Light" ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
            var view = ViewScript.Present(new ShellView { RequestedThemeVariant = variant }, new DesignShellViewModel());
            var surface = Legibility.Resource("SurfaceWindowBrush", variant);

            Assert.Equal(variant, view.Window.ActualThemeVariant);
            Assert.All(Legibility.Texts(view.Window, surface), text => Assert.True(text.Ratio >= 3, $"\"{text.Text}\" is {text.Foreground} on {text.Background}: {text.Ratio:F2}:1"));
        }, Cancellation);

    [Fact]
    public Task TheComposedApplicationShowsItsPageInTheContentRegionAsync() =>
        ui.RunAsync(async () =>
        {
            await using var data = new TemporaryFolder();
            await using var composition = CompositionRoot.Create(plugins.Directory, new AvalaPaths(data.Path));
            var shell = composition.Services.GetRequiredService<ShellViewModel>();
            Application.Current!.DataTemplates.Add(composition.Views);

            try
            {
                shell.Activate();
                var view = ViewScript.Present(new ShellView(), shell);

                Assert.Contains(
                    view.Find("ContentRegion").GetVisualDescendants(),
                    control => control.GetType().Name == "WorkbenchView");
                Assert.False(view.Shows("EmptyContent"));
                Assert.True(view.Shows("SidebarPane"));
                Assert.True(view.Shows("PageNavigation"));
                Assert.Equal(["Overview", "Usage", "Triggers", "Settings"], view.Find<ListBox>("PageNavigation").Items.Cast<IPage>().Select(page => page.Title));
                Assert.Null(view.Find<ListBox>("PageNavigation").SelectedItem);
                Assert.False(view.Shows("InspectorPane"));

                view.Find<ListBox>("PageNavigation").SelectedIndex = 1;
                view.Settle();
                Assert.Equal("Usage", shell.SelectedPage?.Title);
                shell.SelectedPage = shell.Pages[0];
                view.Settle();
                Assert.Null(view.Find<ListBox>("PageNavigation").SelectedItem);
                Assert.Same(shell.Pages[0], shell.SelectedPage);
            }
            finally
            {
                shell.Deactivate();
                Application.Current.DataTemplates.Remove(composition.Views);
            }
        }, Cancellation);

    [Fact]
    public Task AJobChosenInTheSidebarShowsInTheInspectorAsync() =>
        ui.RunAsync(async () =>
        {
            HeadlessApp.Views.Register<JobPicker, JobPickerControl>();
            HeadlessApp.Views.Register<JobInspector, JobInspectorControl>();
            var contexts = new RegionContexts();
            var shell = new ShellViewModel(
                [],
                [new RegionContribution(ShellRegions.Sidebar, 0, new JobPicker(contexts)), new RegionContribution(ShellRegions.Inspector, 0, new JobInspector())],
                contexts,
                new RememberedLayout());
            var view = ViewScript.Present(new ShellView(), shell);
            Avala.Components.UI.Theme.Motion.SetIsReduced(view.Window, true);
            Assert.False(view.Shows("InspectorPane"));

            await shell.PresentsAfterAsync(() => view.Click("PickJob"), () => string.Join(", ", view.VisibleTexts), Cancellation);
            view.Settle();

            Assert.True(view.Shows("InspectorPane"));
            Assert.Contains("Inspecting job-3", view.VisibleTexts);
        }, Cancellation);

    [Fact]
    public Task TheToolbarButtonCollapsesTheSidebarAndControlBackslashBringsItBackAsync() =>
        ui.RunAsync(() =>
        {
            var (view, _) = Composed();

            view.Click("ToggleSidebar");
            var collapsed = (view.Shows("SidebarPane"), view.Shows("ToggleSidebar"), Leading(view));
            view.Press(Key.OemPipe, RawInputModifiers.Control);

            Assert.Equal((false, true, Avala.Components.UI.Theme.Chrome.Leading), collapsed);
            Assert.Equal((true, 0d), (view.Shows("SidebarPane"), Leading(view)));
        }, Cancellation);

    [Fact]
    public Task ANarrowWindowCollapsesTheSidebarAsync() =>
        ui.RunAsync(() =>
        {
            var (view, _) = Composed();

            view.Window.Width = ShellViewModel.NarrowWidth - 100;
            view.Settle();

            Assert.False(view.Shows("SidebarPane"));
        }, Cancellation);

    [Fact]
    public Task FocusModeLeavesOnlyTheContentWithAHintUntilControlShiftFIsPressedAgainAsync() =>
        ui.RunAsync(async () =>
        {
            var (view, shell) = Composed();
            await shell.PresentsAfterAsync(() => view.Click("PickJob"), () => string.Join(", ", view.VisibleTexts), Cancellation);

            view.Press(Key.F, RawInputModifiers.Control | RawInputModifiers.Shift);
            var focused = (view.Shows("SidebarPane"), view.Shows("InspectorPane"), view.Shows("WindowControls"), view.Shows("FocusHint"), Leading(view));
            var hint = view.VisibleTexts;
            view.Press(Key.F, RawInputModifiers.Control | RawInputModifiers.Shift);

            Assert.Equal((false, false, false, true, 0d), focused);
            Assert.Contains("Focus · Ctrl+Shift+F to exit", hint);
            Assert.Equal((true, true, true, false), (view.Shows("SidebarPane"), view.Shows("InspectorPane"), view.Shows("WindowControls"), view.Shows("FocusHint")));
        }, Cancellation);

    [Fact]
    public Task EscapeLeavesFocusModeAsync() =>
        ui.RunAsync(() =>
        {
            var (view, shell) = Composed();

            view.Press(Key.F, RawInputModifiers.Control | RawInputModifiers.Shift);
            view.Press(Key.Escape);

            Assert.Equal((false, true), (shell.IsFocused, view.Shows("SidebarPane")));
        }, Cancellation);

    [Fact]
    public Task FocusModeWidensTheReadingColumnAsync() =>
        ui.RunAsync(() =>
        {
            var (view, _) = Composed();
            view.Window.Width = 1920;
            view.Settle();
            var unfocused = Reading(view);

            view.Press(Key.F, RawInputModifiers.Control | RawInputModifiers.Shift);

            Assert.Equal((880d, 1200d), (unfocused, Reading(view)));
        }, Cancellation);

    [Fact]
    public Task DraggingTheSidebarEdgeResizesTheSidebarAndRemembersItsWidthAsync() =>
        ui.RunAsync(async () =>
        {
            var layout = new RememberedLayout();
            var (view, shell) = Composed(layout);
            var start = Width(view);

            view.Drag("SidebarGrip", 60);
            var widened = Width(view);
            view.Drag("SidebarGrip", -200);
            await shell.Remembering;

            Assert.Equal((WindowLayout.DefaultSidebarWidth, WindowLayout.DefaultSidebarWidth + 60), (start, widened));
            Assert.Equal(WindowLayout.NarrowestSidebar, Width(view));
            Assert.Equal(WindowLayout.Default with { SidebarWidth = WindowLayout.NarrowestSidebar }, layout.Remembered);
        }, Cancellation);

    [Fact]
    public Task DoubleClickingTheSidebarEdgeResetsItsWidthAsync() =>
        ui.RunAsync(async () =>
        {
            var layout = new RememberedLayout();
            var (view, shell) = Composed(layout);
            view.Drag("SidebarGrip", 100);

            view.DoubleClick("SidebarGrip");
            await shell.Remembering;

            Assert.Equal((WindowLayout.DefaultSidebarWidth, WindowLayout.Default), (Width(view), layout.Remembered));
        }, Cancellation);

    [Fact]
    public Task F11TogglesFullScreenAsync() =>
        ui.RunAsync(() =>
        {
            var (view, _) = Composed();

            view.Press(Key.F11);
            var full = view.Window.WindowState;
            view.Press(Key.F11);

            Assert.Equal((WindowState.FullScreen, WindowState.Normal), (full, view.Window.WindowState));
        }, Cancellation);

    [Fact]
    public Task TheReadingColumnGrowsWithTheWindowAndGrowsFurtherWhenWideAsync() =>
        ui.RunAsync(() =>
        {
            var (view, _) = Composed();
            var at1440 = Reading(view);
            view.Window.Width = 1920;
            view.Settle();
            var at1920 = Reading(view);

            Avala.Components.UI.Theme.Reading.SetIsWide(view.Window, true);
            view.Settle();

            Assert.Equal((720d, 880d, 1200d), (at1440, at1920, Reading(view)));
        }, Cancellation);

    private static double Leading(ViewScript view) => ((Thickness)view.Find("ContentPane").Resources["ChromeLeading"]!).Left;

    private static double Reading(ViewScript view) => (double)view.Find("ContentPane").Resources[Avala.Components.UI.Theme.Reading.WidthKey]!;

    private static double Width(ViewScript view) => view.Find("SidebarPane").Bounds.Width;

    private static (ViewScript View, ShellViewModel Shell) Composed() => Composed(new RememberedLayout());

    private static (ViewScript View, ShellViewModel Shell) Composed(RememberedLayout layout)
    {
        HeadlessApp.Views.Register<JobPicker, JobPickerControl>();
        HeadlessApp.Views.Register<JobInspector, JobInspectorControl>();
        var contexts = new RegionContexts();
        var shell = new ShellViewModel(
            [],
            [new RegionContribution(ShellRegions.Sidebar, 0, new JobPicker(contexts)), new RegionContribution(ShellRegions.Inspector, 0, new JobInspector())],
            contexts,
            layout);
        var view = ViewScript.Present(new ShellView { Width = 1440, Height = 900 }, shell);
        Avala.Components.UI.Theme.Motion.SetIsReduced(view.Window, true);

        return (view.Settle(), shell);
    }

    private sealed partial class JobPicker(IRegions regions)
    {
        [RelayCommand]
        private void Pick() => regions.SetContext(ShellRegions.Inspector, "job-3");
    }

    private sealed class JobInspector : IRegionAware<string>, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Text { get; private set; } = "Nothing inspected";

        public void OnRegionContextChanged(Option<string> context)
        {
            Text = context.Match(job => $"Inspecting {job}", () => "Nothing inspected");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        }
    }

    internal sealed class JobPickerControl : Button
    {
        public JobPickerControl()
        {
            Name = "PickJob";
            Content = "Pick job-3";
            this[!CommandProperty] = new ReflectionBinding("PickCommand");
        }

        protected override Type StyleKeyOverride => typeof(Button);
    }

    internal sealed class JobInspectorControl : TextBlock
    {
        public JobInspectorControl() => this[!TextProperty] = new ReflectionBinding("Text");

        protected override Type StyleKeyOverride => typeof(TextBlock);
    }
}
