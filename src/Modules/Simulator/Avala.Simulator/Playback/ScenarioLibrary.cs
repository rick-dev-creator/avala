using System.Collections.Immutable;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Playback;

internal interface IRecordedScenarios
{
    Task<Result<RecordedSession, ReplayError>> LoadAsync(string recording, string workingDirectory, CancellationToken cancellationToken);
}

internal sealed class ScenarioLibrary(IRecordedScenarios recordings)
{
    private ImmutableDictionary<string, int> drawn = ImmutableDictionary<string, int>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);

    public async Task<Scenario> ChooseAsync(string firstMessage, SessionOptions options, CancellationToken cancellationToken) =>
        (await NamedAsync(ScenarioCatalog.NameIn(firstMessage), options, cancellationToken)).Match(scenario => scenario, () => ScenarioCatalog.Reply);

    public Task<Option<Scenario>> NamedAsync(string name, SessionOptions options, CancellationToken cancellationToken) =>
        ReplayRequest.Parse(name).Match(
            async request => Option<Scenario>.Some(await ReplayAsync(request, options, cancellationToken)),
            () => Task.FromResult(ContestScenarios.Families.TryGetValue(name, out var variants) ? Draw(name, variants) : ScenarioCatalog.Named(name)));

    private Option<Scenario> Draw(string family, IReadOnlyList<Scenario> variants) =>
        variants[(ImmutableInterlocked.AddOrUpdate(ref drawn, family, 1, (_, earlier) => earlier + 1) - 1) % variants.Count];

    private async Task<Scenario> ReplayAsync(ReplayRequest request, SessionOptions options, CancellationToken cancellationToken) =>
        !request.NamesAFile
            ? RecordedScript.Unplayable(request, ReplayError.NotFound)
            : (await recordings.LoadAsync(request.Recording, options.WorkingDirectory, cancellationToken)).Match(
                recorded => RecordedScript.Compose(request, recorded, options.Permissions),
                error => RecordedScript.Unplayable(request, error));
}
