using System.Diagnostics;
using System.Text.RegularExpressions;

var options = FlatpakOptions.Parse(args);

if (options is null)
{
    await Console.Error.WriteLineAsync(FlatpakOptions.Usage);
    return 1;
}

var root = Repository.Root();
var archive = Archives.Find(Path.GetFullPath(options.Archive));
var version = Archives.VersionOf(archive);
var work = Path.Combine(root, "artifacts", "flatpak");
var sources = Path.Combine(work, "sources");
var output = Path.GetFullPath(options.Output ?? Path.Combine(root, "artifacts", "packages"));

if (Directory.Exists(work))
{
    Directory.Delete(work, recursive: true);
}

Directory.CreateDirectory(sources);
Directory.CreateDirectory(output);

foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "packaging", "flatpak")))
{
    File.Copy(file, Path.Combine(sources, Path.GetFileName(file)));
}

File.Copy(archive, Path.Combine(sources, "avala-linux-x64.tar.gz"));

var manifest = Path.Combine(sources, $"{Flatpak.AppId}.yml");
var repository = Path.Combine(work, "repo");
var builder = Flatpak.FindBuilder();
await Processes.RunAsync(builder.File, [.. builder.Arguments, "--force-clean", "--disable-rofiles-fuse", $"--state-dir={Path.Combine(work, "state")}", $"--repo={repository}", Path.Combine(work, "build"), manifest], work);

var bundle = Path.Combine(output, $"avala-{version}-linux-x64.flatpak");
File.Delete(bundle);
await Processes.RunAsync(Executables.Find("flatpak"), ["build-bundle", $"--runtime-repo={Flatpak.RuntimeRepository}", repository, bundle, Flatpak.AppId], work);
Directory.Delete(work, recursive: true);

Console.WriteLine($"{Path.GetFileName(bundle)}: {new FileInfo(bundle).Length / 1024.0 / 1024.0:F1} MB");

return 0;

internal sealed record FlatpakOptions(string Archive, string? Output)
{
    public const string Usage =
        "Usage: dotnet run scripts/flatpak.cs -- --archive <avala-<version>-linux-x64.tar.gz or its folder> [--output <folder>]";

    public static FlatpakOptions? Parse(IReadOnlyList<string> args)
    {
        var options = new FlatpakOptions(string.Empty, null);
        string[] rest = [.. args];

        while (rest.Length > 0)
        {
            (options, rest) = rest switch
            {
                ["--archive", var archive, .. var next] => (options with { Archive = archive }, next),
                ["--output", var output, .. var next] => (options with { Output = output }, next),
                _ => (options with { Archive = string.Empty }, []),
            };
        }

        return options.Archive.Length > 0 ? options : null;
    }
}

internal sealed record Builder(string File, IReadOnlyList<string> Arguments);

internal static class Flatpak
{
    public const string AppId = "io.github.rick_dev_creator.Avala";

    public const string RuntimeRepository = "https://dl.flathub.org/repo/flathub.flatpakrepo";

    public static Builder FindBuilder() =>
        Executables.TryFind("flatpak-builder") is { } builder
            ? new Builder(builder, [])
            : new Builder(Executables.Find("flatpak"), ["run", "--filesystem=host", "org.flatpak.Builder"]);
}

internal static class Executables
{
    public static string Find(string name) =>
        TryFind(name) ?? throw new InvalidOperationException($"{name} was not found on PATH. See docs/release.md, Flatpak.");

    public static string? TryFind(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, name))
            .FirstOrDefault(File.Exists);
}

internal static partial class Archives
{
    public static string Find(string path) =>
        Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "avala-*-linux-x64.tar.gz").SingleOrDefault()
                ?? throw new InvalidOperationException($"{path} holds no single avala-<version>-linux-x64.tar.gz.")
            : File.Exists(path) ? path : throw new FileNotFoundException($"{path} does not exist.");

    public static string VersionOf(string archive) =>
        LinuxArchive().Match(Path.GetFileName(archive)) is { Success: true } match
            ? match.Groups["version"].Value
            : throw new InvalidOperationException($"{Path.GetFileName(archive)} is not named avala-<version>-linux-x64.tar.gz.");

    [GeneratedRegex(@"^avala-(?<version>\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?)-linux-x64\.tar\.gz$")]
    private static partial Regex LinuxArchive();
}

internal sealed record ProcessResult(bool Succeeded, string Output);

internal static class Processes
{
    public static async Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> arguments, string directory, bool allowFailure = false)
    {
        var info = new ProcessStartInfo(file)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException($"{file} could not start.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var result = new ProcessResult(process.ExitCode == 0, (await output).Trim());

            return result.Succeeded || allowFailure
                ? result
                : throw new InvalidOperationException($"{file} {string.Join(' ', arguments)} exited with {process.ExitCode}: {result.Output}\n{(await error).Trim()}");
        }
        catch (System.ComponentModel.Win32Exception) when (allowFailure)
        {
            return new ProcessResult(false, string.Empty);
        }
    }
}

internal static class Repository
{
    public static string Root()
    {
        for (var folder = new DirectoryInfo(Directory.GetCurrentDirectory()); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Avala.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("Run the script from inside the Avala repository.");
    }
}
