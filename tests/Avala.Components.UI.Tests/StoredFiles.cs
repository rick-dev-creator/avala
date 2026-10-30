using Avala.Testing;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Avala.Components.UI.Tests;

internal static class StoredFiles
{
    public static async Task<IReadOnlyList<IStorageFile>> WrittenAsync(TopLevel top, TemporaryFolder folder, params string[] names)
    {
        List<IStorageFile> files = [];

        foreach (var name in names)
        {
            var path = Path.Combine(folder.Path, name);
            await File.WriteAllTextAsync(path, name, TestContext.Current.CancellationToken);
            files.Add(await top.StorageProvider.TryGetFileFromPathAsync(path) ?? throw new InvalidOperationException($"The headless storage provider cannot open {path}."));
        }

        return files;
    }
}
