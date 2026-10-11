using Avala.Notifications.Contracts;
using Avala.Notifications.NotificationFiles;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Notifications.Tests.NotificationFiles;

public sealed class NotificationFileStoreTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutAFileTheSettingsAreAbsentAsync()
    {
        await using var data = new TemporaryFolder();

        var file = await new NotificationFileStore(new AvalaPaths(data.Path)).ReadAsync(Cancellation);

        Assert.Equal((Path.Combine(data.Path, "notifications.json"), NotificationFileStatus.Absent, false), (file.Path, file.Status, file.Settings.IsSome));
    }

    [Fact]
    public async Task ARejectedFileSaysWhyAndCarriesNoSettingsAsync()
    {
        await using var data = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(data.Path, NotificationFileStore.FileName), """{ "ntfy": { "topic": "t" }, "volume": 11 }""", Cancellation);

        var file = await new NotificationFileStore(new AvalaPaths(data.Path)).ReadAsync(Cancellation);

        Assert.Equal((NotificationFileStatus.Rejected, NotificationError.UnknownField, false), (file.Status, Outcomes.Present(file.Error), file.Settings.IsSome));
    }

    [Fact]
    public async Task WrittenSettingsReplaceTheFileAndAreAppliedAsync()
    {
        await using var data = new TemporaryFolder();
        var store = new NotificationFileStore(new AvalaPaths(data.Path));
        var settings = new NotificationSettings(ChannelKind.Ntfy) { Ntfy = new NtfyTarget(NtfyTarget.DefaultServer, "avala-k3j9") };

        var file = Outcomes.Succeeds(await store.WriteAsync(settings, Cancellation));

        Assert.Equal((NotificationFileStatus.Applied, "avala-k3j9"), (file.Status, Outcomes.Present(Outcomes.Present(file.Settings).Ntfy).Topic));
        Assert.Equal([NotificationFileStore.FileName], Directory.GetFiles(data.Path).Select(Path.GetFileName));
    }
}
