using Avala.Notifications.Contracts;
using Avala.Sdk;
using Avala.Testing.UI;
using Avala.Workbench.Machine;
using Avala.Workbench.Settings;
using Avalonia.Controls;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Views;

public sealed class NotificationsViewScripts(HeadlessUi ui)
{
    private static readonly string[] Events = ["EventDecision", "EventHeld", "EventBudget", "EventFailed", "EventLoopEnded", "EventReview"];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheSectionShowsTheChannelTheEventsTheDetailThePreviewAndTheLastDeliveryAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignNotificationsViewModel());

            Assert.Equal("notifications.json: applied", view.TextOf("NotificationsFile"));
            Assert.Equal(("ntfy", "https://ntfy.sh/avala-k3j9m2q7xw5rbt4na6ceyd8h"), (view.TextOf("Channel"), view.TextOf("Destination")));
            Assert.Equal([true, true, true, true, true, false], Events.Select(name => view.Find<CheckBox>(name).IsChecked == true));
            Assert.Equal((true, false), (view.HasClass("ChooseMinimal", "selected"), view.HasClass("ChooseDetailed", "selected")));
            Assert.Contains("\"title\":\"A decision needs you · ledger-api\"", view.Find<SelectableTextBlock>("Preview").Text, StringComparison.Ordinal);
            Assert.Equal("Sent a decision at 09:31", view.TextOf("LastDelivery"));
            Assert.False(view.Shows("SetUpNotifications"));
        }, Cancellation);

    [Fact]
    public Task WithoutAFileOnlySetUpIsOfferedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignNotificationsViewModel { IsSetUp = false, HasFile = false, Status = "not set up" });

            Assert.True(view.Shows("SetUpNotifications"));
            Assert.False(view.Shows("OpenNotificationsFile"));
            Assert.False(view.Shows("NotificationSettings"));
        }, Cancellation);

    [Fact]
    public Task ClickingSetUpAReviewEventDetailedAndSendTestInvokesTheSectionsCommandsAsync() =>
        ui.RunAsync(async () =>
        {
            var notifications = new FakeNotifications();
            var section = new NotificationsViewModel(new PhoneNotifications(notifications, new SettingsFiles(new FakeOpener(), new AvalaPaths("/data")), new FakeTimeProvider(FakeNotifications.Now)));
            await section.LoadAsync(Cancellation);
            var view = Screen.Show(section);

            view.Click("SetUpNotifications").Settle();
            view.Click("EventReview").Settle();
            view.Click("ChooseDetailed").Settle();
            view.Click("SendTest").Settle();

            Assert.Equal([(true, DetailLevel.Minimal), (true, DetailLevel.Detailed)], notifications.Choices.Select(choice => (choice.Events.Contains(NotificationKind.Review), choice.Detail)));
            Assert.Equal((true, true), (view.Find<CheckBox>("EventReview").IsChecked == true, view.HasClass("ChooseDetailed", "selected")));
            Assert.Equal(NotificationPhrases.TestSent, view.TextOf("TestResult"));
        }, Cancellation);
}
