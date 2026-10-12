using System.Collections.ObjectModel;
using Avala.Components.UI.Theme;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Avala.Components.UI.Tests;

public sealed class ArrivalsScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task InAListWhereOnlyNewArrivalsEnterTheItemsItAlreadyHadHoldStillAsync() =>
        ui.RunAsync(() =>
        {
            var items = new ObservableCollection<string> { "Old" };
            var list = new ItemsControl
            {
                ItemsSource = items,
                ItemTemplate = new FuncDataTemplate<string>((name, _) => Entering(name)),
                ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            };
            Arrivals.SetOnlyNewEnter(list, true);
            var view = ViewScript.Show(list, new object());

            items.Add("New");
            view.Settle();

            Assert.Equal((1d, 0d), (view.Find("Old").Opacity, view.Find("New").Opacity));
        }, Cancellation);

    private static Border Entering(string name)
    {
        var entry = new Border { Name = name, Width = 10, Height = 10 };
        Motion.SetEnter(entry, MotionEntrance.Fade);

        return entry;
    }
}
