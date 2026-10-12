using Avala.Components.Privacy;
using Avala.Sdk.Telemetry;
using Avala.Workbench.Telemetry;

namespace Avala.Workbench.Onboarding;

internal interface IPrivacyStepViewModel : IOnboardingStep
{
    IPersonalDetailsViewModel PersonalDetails { get; }

    ITelemetryViewModel Statistics { get; }

    Task ConcludeAsync(CancellationToken cancellationToken);
}

internal sealed class PrivacyStepViewModel(IPersonalDetailsViewModel personalDetails, ITelemetryViewModel statistics, ITelemetry telemetry) : IPrivacyStepViewModel
{
    public IPersonalDetailsViewModel PersonalDetails => personalDetails;

    public ITelemetryViewModel Statistics => statistics;

    public Task LoadAsync(CancellationToken cancellationToken) => statistics.LoadAsync(cancellationToken);

    public async Task ConcludeAsync(CancellationToken cancellationToken) => _ = await telemetry.ClaimNoticeAsync(cancellationToken);
}
