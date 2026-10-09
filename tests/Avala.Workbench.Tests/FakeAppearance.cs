using Avala.Sdk;
using Avala.Sdk.Appearance;

namespace Avala.Workbench.Tests;

internal sealed class FakeAppearance(AppearancePreference? initial = null) : IAppearance
{
    private AppearancePreference current = initial ?? AppearancePreference.Default;

    public List<AppearancePreference> Changes { get; } = [];

    public AppearanceError? Refusal { get; init; }

    public AppearanceFileStatus File { get; init; } = initial is null ? AppearanceFileStatus.Absent : AppearanceFileStatus.Applied;

    public AppearanceError? Fault { get; init; }

    public ValueTask<AppearanceSettings> ReadAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new AppearanceSettings(current, File, Fault is { } fault ? fault : Option<AppearanceError>.None));

    public ValueTask<Result<AppearanceSettings, AppearanceError>> ChangeAsync(AppearancePreference preference, CancellationToken cancellationToken)
    {
        if (Refusal is { } refusal)
        {
            return ValueTask.FromResult(Result<AppearanceSettings, AppearanceError>.Failure(refusal));
        }

        Changes.Add(preference);
        current = preference;

        return ValueTask.FromResult<Result<AppearanceSettings, AppearanceError>>(new AppearanceSettings(preference, AppearanceFileStatus.Applied, Option<AppearanceError>.None));
    }
}
