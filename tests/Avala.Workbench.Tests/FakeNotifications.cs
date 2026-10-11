using Avala.Notifications.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Tests;

internal sealed class FakeNotifications : INotifications
{
    public static readonly DateTimeOffset Now = new(2026, 10, 9, 13, 0, 0, TimeSpan.Zero);

    public NotificationFile File { get; set; } = new("/data/notifications.json", NotificationFileStatus.Absent);

    public List<NotificationChoices> Choices { get; } = [];

    public List<NotificationRecord> Records { get; } = [];

    public Option<NotificationError> Refusal { get; set; }

    public static NotificationFile Applied(NotificationSettings settings) =>
        new("/data/notifications.json", NotificationFileStatus.Applied) { Settings = settings, Preview = $"POST preview {settings.Detail}" };

    public static NotificationSettings Ntfy(string topic) => new(ChannelKind.Ntfy) { Ntfy = new NtfyTarget(NtfyTarget.DefaultServer, topic) };

    public ValueTask<NotificationFile> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(File);

    public ValueTask<Result<NotificationFile, NotificationError>> SetUpAsync(CancellationToken cancellationToken)
    {
        File = Applied(Ntfy("avala-generated"));

        return ValueTask.FromResult(Result<NotificationFile, NotificationError>.Success(File));
    }

    public ValueTask<Result<NotificationFile, NotificationError>> ChooseAsync(NotificationChoices choices, CancellationToken cancellationToken)
    {
        Choices.Add(choices);
        File = File.Settings.Match(settings => Applied(settings with { Events = choices.Events, Detail = choices.Detail }), () => File);

        return ValueTask.FromResult(Result<NotificationFile, NotificationError>.Success(File));
    }

    public ValueTask<Result<NotificationRecord, NotificationError>> SendTestAsync(CancellationToken cancellationToken)
    {
        var record = new NotificationRecord(Guid.NewGuid(), NotificationKind.Test, Severity.Info, Now, Refusal.IsSome ? NotificationOutcome.Failed : NotificationOutcome.Sent)
        {
            Attempts = 1,
            Error = Refusal,
        };
        Records.Add(record);

        return ValueTask.FromResult(Refusal.Match(Result<NotificationRecord, NotificationError>.Failure, () => Result<NotificationRecord, NotificationError>.Success(record)));
    }

    public IReadOnlyList<NotificationRecord> Recent() => Records;
}
