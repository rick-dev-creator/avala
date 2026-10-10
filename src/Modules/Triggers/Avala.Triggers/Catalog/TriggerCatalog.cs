using System.Collections.Immutable;
using Avala.Sdk;
using Avala.Triggers.Contracts;
using Avala.Triggers.Declarations;

namespace Avala.Triggers.Catalog;

internal sealed record TriggerFileContent(IReadOnlyList<string> Repositories, IReadOnlyList<TriggerDeclaration> Triggers)
{
    public static TriggerFileContent Empty { get; } = new([], []);
}

internal sealed record FileReading(TriggerFile File, IReadOnlyList<TriggerDeclaration> Triggers, IReadOnlyList<string> Repositories);

internal interface ITriggerFiles
{
    ValueTask<FileReading> MachineAsync(CancellationToken cancellationToken);

    ValueTask<FileReading> RepositoryAsync(string repository, CancellationToken cancellationToken);
}

internal sealed record CatalogSnapshot(ImmutableList<TriggerDeclaration> Triggers, ImmutableList<TriggerFile> Files)
{
    public static CatalogSnapshot Empty { get; } = new([], []);

    public Option<TriggerDeclaration> Find(TriggerId id) => Triggers.FirstOrDefault(trigger => trigger.Id == id).ToOption();

    public Option<TriggerDeclaration> Hook(string name) =>
        Triggers.FirstOrDefault(trigger => trigger.Webhook.IsSome && trigger.Id.Scope == TriggerId.Machine && trigger.Id.Name == name).ToOption();
}

internal sealed class TriggerCatalog(ITriggerFiles files)
{
    private CatalogSnapshot snapshot = CatalogSnapshot.Empty;

    public CatalogSnapshot Current => Volatile.Read(ref snapshot);

    public async Task<CatalogSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        var machine = await files.MachineAsync(cancellationToken);
        var readings = new List<FileReading> { machine };

        foreach (var repository in machine.Repositories)
        {
            readings.Add(await files.RepositoryAsync(repository, cancellationToken));
        }

        var loaded = new CatalogSnapshot([.. readings.SelectMany(reading => reading.Triggers)], [.. readings.Select(reading => reading.File)]);
        Volatile.Write(ref snapshot, loaded);

        return loaded;
    }

    public async Task<Option<TriggerDeclaration>> CurrentAsync(TriggerId id, CancellationToken cancellationToken) =>
        (await LoadAsync(cancellationToken)).Find(id);
}
