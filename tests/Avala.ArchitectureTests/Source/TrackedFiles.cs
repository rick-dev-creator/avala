using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.ArchitectureTests.Source;

internal static class TrackedFiles
{
    public const string PrivateMethodFolder = ".method/";

    public static IEnumerable<string> InPrivateMethod(IEnumerable<string> trackedPaths) =>
        trackedPaths.Where(path => path.StartsWith(PrivateMethodFolder, StringComparison.Ordinal));

    public static async Task<IReadOnlyList<string>> ListAsync(string repository, CancellationToken cancellationToken)
    {
        var data = Directory.CreateTempSubdirectory("avala-tracked-");

        try
        {
            await using var services = new ServiceCollection().AddRuntime(new AvalaPaths(data.FullName)).BuildServiceProvider();
            var listing = await services.GetRequiredService<IProcessRunner>()
                .RunAsync(new ProcessRequest("git", ["-C", repository, "ls-files", "-z"]), cancellationToken);

            return listing.Match(
                outcome => outcome.Succeeded
                    ? outcome.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                    : throw new InvalidOperationException($"git ls-files failed in {repository}: {outcome.Error}"),
                error => throw new InvalidOperationException($"git could not run in {repository}: {error}"));
        }
        finally
        {
            data.Delete(recursive: true);
        }
    }
}
