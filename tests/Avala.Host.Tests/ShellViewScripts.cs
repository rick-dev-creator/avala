using System.ComponentModel;
using Avala.Host.Composition;
using Avala.Sdk;
using Avala.Sdk.Regions;
using Avala.Shell;
using Avala.Shell.Regions;
using Avala.Testing;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
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
            Assert.False(view.Shows("PageNavigation"));
            Assert.Superset(
                new HashSet<string>(["Avala", "⌘K", "jump", "Add invoice PDF endpoint", "Fix JPY rounding in invoice totals", "claude-work · 5h", "88% · resets 16:20", "Inspector", "claude-personal · 5h", "Nothing to show yet"]),
                view.VisibleTexts.ToHashSet());
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
                Assert.False(view.Shows("InspectorPane"));
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
                contexts);
            var view = ViewScript.Present(new ShellView(), shell);
            Assert.False(view.Shows("InspectorPane"));

            await shell.PresentsAfterAsync(() => view.Click("PickJob"), () => string.Join(", ", view.VisibleTexts), Cancellation);
            view.Settle();

            Assert.True(view.Shows("InspectorPane"));
            Assert.Contains("Inspecting job-3", view.VisibleTexts);
        }, Cancellation);

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
