using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Resources.Tracking;
using Avala.Resources.Usage;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;

namespace Avala.Resources.Scratch;

internal sealed class TemporaryFolders(AvalaPaths paths, ResourceBook book) : IProcessEnvironment, IHandle<JobProgressed>
{
    public ValueTask<IReadOnlyDictionary<string, string>> ForAsync(string home, CancellationToken cancellationToken)
    {
        var folder = ScratchFolders.Of(paths, home);

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(ImmutableDictionary<string, string>.Empty);
        }

        return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
            ScratchFolders.Variables.ToImmutableDictionary(variable => variable, _ => folder, StringComparer.Ordinal));
    }

    public ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Status.Ended())
        {
            foreach (var home in book.Attribution.HomesOf(integrationEvent.Job))
            {
                Remove(ScratchFolders.Of(paths, home));
            }
        }

        return ValueTask.CompletedTask;
    }

    private static void Remove(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
