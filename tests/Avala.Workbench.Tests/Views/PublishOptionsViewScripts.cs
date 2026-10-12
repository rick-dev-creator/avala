using Avala.Testing.UI;
using Avala.Workbench.RepositoryChoice;
using Avala.Workbench.Tests.RepositoryChoice;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class PublishOptionsViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ARepositoryIsPrivateUntilPublicIsChosenAsync() =>
        ui.RunAsync(async () =>
        {
            var options = new PublishOptionsViewModel(new FakePublishing());
            await options.LoadAsync("invoices", CancellationToken.None);
            var view = Screen.Show(options).Settle();
            var initially = options.IsPrivate;

            view.Click("PublishPublic").Settle();

            Assert.Equal((true, false, (bool?)false), (initially, options.IsPrivate, view.Find<RadioButton>("PublishPrivate").IsChecked));
        }, TestContext.Current.CancellationToken);
}
