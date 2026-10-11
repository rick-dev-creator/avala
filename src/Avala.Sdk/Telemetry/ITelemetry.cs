namespace Avala.Sdk.Telemetry;

public enum TelemetryStatus
{
    NotConfigured,
    On,
    Off,
    OffByEnvironment,
}

public sealed record TelemetryState(TelemetryStatus Status, Option<string> InstallId, Option<string> OptedOutBy)
{
    public static TelemetryState NotConfigured { get; } = new(TelemetryStatus.NotConfigured, Option<string>.None, Option<string>.None);
}

public interface ITelemetry
{
    Task<TelemetryState> ReadAsync(CancellationToken cancellationToken);

    Task<string> PreviewAsync(CancellationToken cancellationToken);

    Task<TelemetryState> TurnAsync(bool on, CancellationToken cancellationToken);

    Task<TelemetryState> ResetInstallIdAsync(CancellationToken cancellationToken);

    Task<bool> ClaimNoticeAsync(CancellationToken cancellationToken);
}
