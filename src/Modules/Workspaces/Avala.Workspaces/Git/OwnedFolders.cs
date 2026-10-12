namespace Avala.Workspaces.Git;

internal static class OwnedFolders
{
    public static bool IsPlainName(string name) =>
        name.Length > 0
        && name is not "." and not ".."
        && name.IndexOfAny(['/', '\\']) < 0
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    public static void Remove(string folder)
    {
        var root = new DirectoryInfo(folder);

        if (!root.Exists || IsLink(root))
        {
            return;
        }

        Empty(root);
        root.Delete();
    }

    private static void Empty(DirectoryInfo folder)
    {
        foreach (var entry in folder.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, RecurseSubdirectories = false }))
        {
            if (entry is DirectoryInfo directory && !IsLink(directory))
            {
                Empty(directory);
            }

            if (entry is FileInfo { IsReadOnly: true } file && !IsLink(file))
            {
                file.IsReadOnly = false;
            }

            entry.Delete();
        }
    }

    private static bool IsLink(FileSystemInfo entry) => entry.LinkTarget is not null || entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
}
