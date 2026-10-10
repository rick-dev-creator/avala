using Avala.Testing.UI;
using Avala.Workbench.Inspector;
using Avala.Workbench.Review;
using Avala.Workbench.Settings;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class PullRequestSectionViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ThePullRequestSectionIsFoldedWithItsNumberAndOpensToItsStateChecksReviewsAndWakeUpsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignPullRequestSectionViewModel());
            var folded = Sections.Row(view);

            Sections.ClickHeader(view);

            Assert.Equal(((object?)"Pull request", (string?)"#7 · Watching", false), folded);
            Assert.Equal(("Watching · next check 14:05", "1 passed · 1 failed · 0 pending", "No conflicts"), (view.TextOf("Status"), view.TextOf("Checks"), view.TextOf("Mergeability")));
            Assert.Equal((2, 1, 1), (view.Find<ItemsControl>("CheckLines").ItemCount, view.Find<ItemsControl>("Reviews").ItemCount, view.Find<ItemsControl>("WakeUps").ItemCount));
            Assert.True(view.Shows("OpenLink"));
            Assert.False(view.Shows("Notice"));
        }, Cancellation);

    [Fact]
    public Task ClickingOpenInBrowserAndCheckNowRunTheirCommandsAsync() =>
        ui.RunAsync(() =>
        {
            var section = new CountingSection();
            var view = Sections.Open(Screen.Show(section));

            view.Click("OpenLink").Click("CheckNow");

            Assert.Equal((1, 1), (section.Opened, section.Checked));
        }, Cancellation);

    private sealed class CountingSection : IPullRequestSectionViewModel
    {
        public CountingSection()
        {
            OpenLinkCommand = new AsyncRelayCommand(() =>
            {
                Opened++;

                return Task.CompletedTask;
            });
            CheckNowCommand = new AsyncRelayCommand(() =>
            {
                Checked++;

                return Task.CompletedTask;
            });
        }

        public int Opened { get; private set; }

        public int Checked { get; private set; }

        public bool IsLoaded => true;

        public bool HasPullRequest => true;

        public string Fact => "#7";

        public string Title => "Pull request #7 on github";

        public string Link => "https://github.com/octo/shop/pull/7";

        public string Status => "Watching";

        public string Policy => string.Empty;

        public string Checks => "No checks";

        public IReadOnlyList<string> CheckLines => [];

        public string Mergeability => string.Empty;

        public IReadOnlyList<string> Reviews => [];

        public IReadOnlyList<string> WakeUps => [];

        public string Notice => string.Empty;

        public IAsyncRelayCommand OpenLinkCommand { get; }

        public IAsyncRelayCommand CheckNowCommand { get; }
    }
}

public sealed class ForgeConnectionViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheMachineSettingsListTheForgesWithTheirCredentialReferenceAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignMachineSettingsViewModel());

            Assert.Equal(1, view.Find<ItemsControl>("Forges").ItemCount);
            Assert.Contains("token in $GITHUB_TOKEN", view.VisibleTexts);
            Assert.Equal("forges.json · checks every 60s · GitHub, Gitea, Forgejo installed", view.TextOf("ForgesFile"));
            Assert.False(view.Shows("NoForges"));
        }, Cancellation);

    [Fact]
    public Task AForgeConnectionShowsItsForgeUrlCredentialAndAProblemOnlyWhenItHasOneAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignForgeConnectionViewModel());

            Assert.Equal(("github", "GitHub", "https://api.github.com/", "token in $GITHUB_TOKEN"), (view.TextOf("ForgeName"), view.TextOf("Forge"), view.TextOf("Url"), view.TextOf("Credential")));
            Assert.False(view.Shows("Problem"));
        }, Cancellation);
}
