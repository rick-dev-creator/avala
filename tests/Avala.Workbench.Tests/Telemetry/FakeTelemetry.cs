using Avala.Sdk;
using Avala.Sdk.Telemetry;

namespace Avala.Workbench.Tests.Telemetry;

internal sealed class FakeTelemetry : ITelemetry
{
    private bool noticeShown;
    private int resets;

    public TelemetryState State { get; set; } = new(TelemetryStatus.On, "3f2b8c1e-7d4a-4e09-9b6f-52c1a0d8e7f4", Option<string>.None);

    public static string Payload(string id) => $$"""{ "sessionId": "{{id}}", "eventName": "active" }""";

    public Task<TelemetryState> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(State);

    public Task<string> PreviewAsync(CancellationToken cancellationToken) => Task.FromResult(State.InstallId.Match(Payload, () => string.Empty));

    public Task<TelemetryState> TurnAsync(bool on, CancellationToken cancellationToken)
    {
        noticeShown = true;
        State = State with { Status = on ? TelemetryStatus.On : TelemetryStatus.Off };

        return Task.FromResult(State);
    }

    public Task<TelemetryState> ResetInstallIdAsync(CancellationToken cancellationToken)
    {
        State = State with { InstallId = $"reset-{++resets}" };

        return Task.FromResult(State);
    }

    public Task<bool> ClaimNoticeAsync(CancellationToken cancellationToken)
    {
        var claimed = State.Status == TelemetryStatus.On && !noticeShown;
        noticeShown = true;

        return Task.FromResult(claimed);
    }
}
