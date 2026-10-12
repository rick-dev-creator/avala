using Avala.Components.Privacy;
using Avala.Testing;
using Avala.Workbench.Accounts;
using Avala.Workbench.Onboarding;
using Avala.Workbench.Submitting;
using Avala.Workbench.Telemetry;
using Avala.Workbench.Tests.Accounts;
using Avala.Workbench.Tests.RepositoryChoice;
using Avala.Workbench.Tests.Telemetry;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Onboarding;

internal sealed class Touring
{
    public FakeOnboarding Onboarding { get; } = new();

    public FakeTelemetry Telemetry { get; } = new();

    public FakeConnections Connections { get; } = new("claude-work");

    public FakePreview Preview { get; } = new();

    public FakeSourceControl Forges { get; } = new();

    public FakeProjects Projects { get; } = new();

    public FakeFolderPicker Picker { get; } = new();

    public FakeClipboard Clipboard { get; } = new();

    public FakeAppearance Appearance { get; } = new();

    public WeakReferenceMessenger Messenger { get; } = new();

    public AgentsStepViewModel Agents() => new(Launching.Launch(new SubmittingJobs(), Connections, Preview, new FakePolicies()), Clipboard);

    public CodeStepViewModel Code() => new(new ProjectsPlace(Projects, Picker), new SourceControlViewModel(Forges.Source, new ConnectSheetViewModel(Forges.Source)));

    public PrivacyStepViewModel Privacy() => new(new PersonalDetailsViewModel(Appearance, InlineUiDispatcher.Instance), new TelemetryViewModel(Telemetry), Telemetry);

    public OnboardingViewModel Tour() => new(Onboarding, new OnboardingSteps(new WelcomeStepViewModel(), Agents(), Code(), Privacy()), Messenger);
}
