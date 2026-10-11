using Avala.Notifications.Contracts;
using Avala.Sdk;
using Avala.Workbench.Machine;
using Avala.Workbench.Settings;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Settings;

public sealed class NotificationsViewModelScripts
{
    private readonly FakeNotifications notifications = new();
    private readonly FakeOpener opener = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutAFileTheSectionOffersSetUpWhichShowsAnNtfyTopicOnNtfyShAsync()
    {
        var section = await SectionAsync();
        var before = (section.Status, section.CanSetUp, section.IsSetUp, section.SendTestCommand.CanExecute(null));

        await section.SetUpCommand.ExecuteAsync(null);

        Assert.Equal(("not set up", true, false, false), before);
        Assert.Equal(("applied", "ntfy", "https://ntfy.sh/avala-generated", true), (section.Status, section.Channel, section.Destination, section.IsSetUp));
        Assert.Equal("No token: anyone who knows the topic can read what is sent", section.Secret);
    }

    [Fact]
    public async Task AnAppliedFileShowsItsEventsDetailQuietHoursSecretPreviewAndLastDeliveryAsync()
    {
        var settings = FakeNotifications.Ntfy("avala-team") with
        {
            Ntfy = new NtfyTarget(new Uri("https://ntfy.example.com"), "avala-team") { TokenEnv = "AVALA_NTFY_TOKEN" },
            Events = [NotificationKind.Decision, NotificationKind.Review],
            Detail = DetailLevel.Detailed,
            Quiet = new QuietHours(new TimeOnly(22, 0), new TimeOnly(7, 0)) { Days = [DayOfWeek.Friday] },
        };
        notifications.File = FakeNotifications.Applied(settings) with { Secret = SecretStatus.NotSet };
        notifications.Records.Add(new NotificationRecord(Guid.NewGuid(), NotificationKind.Decision, Severity.Urgent, FakeNotifications.Now, NotificationOutcome.Failed) { Attempts = 4, Error = NotificationError.Unreachable });

        var section = await SectionAsync();

        Assert.Equal((true, false, false, false, false, true, true), (section.Decision, section.Held, section.Budget, section.Failed, section.LoopEnded, section.Review, section.IsDetailed));
        Assert.Equal("AVALA_NTFY_TOKEN is not set: nothing is sent until it is", section.Secret);
        Assert.Equal("22:00–07:00 Fri, urgent notices still sent, the rest in one digest after", section.Quiet);
        Assert.Equal(("POST preview Detailed", "Failed to send a decision at 13:00 after 4 attempts: the server could not be reached"), (section.Preview, section.LastDelivery));
    }

    [Fact]
    public async Task TogglingAnEventOrChoosingTheDetailSavesTheChoicesAsync()
    {
        notifications.File = FakeNotifications.Applied(FakeNotifications.Ntfy("avala-team"));
        var section = await SectionAsync();

        await section.ToggleEventCommand.ExecuteAsync("Failed");
        await section.ChooseDetailCommand.ExecuteAsync("Detailed");

        Assert.Equal(
            [
                new NotificationChoices([NotificationKind.Decision, NotificationKind.Held, NotificationKind.Budget, NotificationKind.LoopEnded], DetailLevel.Minimal),
                new NotificationChoices([NotificationKind.Decision, NotificationKind.Held, NotificationKind.Budget, NotificationKind.LoopEnded], DetailLevel.Detailed),
            ],
            notifications.Choices,
            (expected, actual) => expected.Events.SequenceEqual(actual.Events) && expected.Detail == actual.Detail);
        Assert.Equal((false, true, "POST preview Detailed"), (section.Failed, section.IsDetailed, section.Preview));
    }

    [Fact]
    public async Task SendingATestSaysWhetherItWentAndShowsItAsTheLastDeliveryAsync()
    {
        notifications.File = FakeNotifications.Applied(FakeNotifications.Ntfy("avala-team"));
        var section = await SectionAsync();

        await section.SendTestCommand.ExecuteAsync(null);
        var sent = (section.TestResult, section.LastDelivery);
        notifications.Refusal = NotificationError.MissingSecret;
        await section.SendTestCommand.ExecuteAsync(null);

        Assert.Equal((NotificationPhrases.TestSent, "Sent a test at 13:00"), sent);
        Assert.Equal("The test was not sent: the variable holding the secret is not set.", section.TestResult);
    }

    [Fact]
    public async Task ARejectedFileSaysWhyAndOffersToOpenItAsync()
    {
        notifications.File = new NotificationFile("/data/notifications.json", NotificationFileStatus.Rejected) { Error = NotificationError.InsecureAddress };
        var section = await SectionAsync();

        await section.OpenFileCommand.ExecuteAsync(null);

        Assert.Equal(("rejected: plain http is allowed only on this computer", false, false), (section.Status, section.IsSetUp, section.CanSetUp));
        Assert.Equal([Path.Combine("/data", "notifications.json")], opener.Opened);
    }

    private async Task<NotificationsViewModel> SectionAsync()
    {
        var section = new NotificationsViewModel(new PhoneNotifications(notifications, new SettingsFiles(opener, new AvalaPaths("/data")), new FakeTimeProvider(FakeNotifications.Now)));
        await section.LoadAsync(Cancellation);

        return section;
    }
}
