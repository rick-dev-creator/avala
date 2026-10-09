using Avala.Testing.UI;
using Avala.Workbench.Updates;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class UpdateNoticeViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheNoticeIsHiddenWhileNoUpdateIsFoundAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new Notice(IsShown: false, string.Empty));

            Assert.False(view.Shows("Open"));
        }, Cancellation);

    [Fact]
    public Task AFoundUpdateIsNamedAndClickingItOpensItsPageAsync() =>
        ui.RunAsync(() =>
        {
            var notice = new Notice(IsShown: true, "Version 0.2.0 is available");
            var view = Screen.Show(notice);

            view.Click("Open");

            Assert.Equal("Version 0.2.0 is available", view.TextOf("Text"));
            Assert.Equal(1, notice.Opened);
            Assert.False(view.Shows("Note"));
        }, Cancellation);

    private sealed record Notice(bool IsShown, string Text) : IUpdateNoticeViewModel
    {
        public int Opened { get; private set; }

        public string Note => string.Empty;

        public IAsyncRelayCommand OpenCommand => new AsyncRelayCommand(() =>
        {
            Opened++;
            return Task.CompletedTask;
        });
    }
}
