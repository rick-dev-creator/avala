using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

var options = InstallerOptions.Parse(args);

if (options is null)
{
    await Console.Error.WriteLineAsync(InstallerOptions.Usage);
    return 1;
}

var root = Repository.Root();
var target = Targets.Of(options.Rid);
var archive = Archives.Find(Path.GetFullPath(options.Archive), target);
var version = Archives.VersionOf(archive, target);
var work = Path.Combine(root, "artifacts", "installer", target.Rid);
var output = Path.GetFullPath(options.Output ?? Path.Combine(root, "artifacts", "packages"));
var app = Path.Combine(work, "app");
var releases = Path.Combine(work, "releases");

Console.WriteLine($"Making the Velopack installers of Avala {version} for {target.Rid} from {Path.GetFileName(archive)}.");
Folders.Recreate(work);
Directory.CreateDirectory(releases);
Directory.CreateDirectory(output);

var packDir = await Archives.ExtractAsync(archive, app);
await Velopack.DownloadPreviousAsync(root, target, version, releases);
await Velopack.PackAsync(root, target, version, packDir, releases);
var made = await Feed.PublishAsync(releases, target, version, output);

if (options.Smoke && made.AppImage is { } appImage)
{
    await Smoke.RunAsync(appImage, version);
}

foreach (var file in made.Offered)
{
    var hash = await Checksums.WriteAsync(file);
    await Report.LineAsync(file, $"sha256 {hash}");
}

foreach (var file in made.Feed)
{
    await Report.LineAsync(file, "update feed");
}

return 0;

internal sealed record InstallerOptions(string Rid, string Archive, string? Output, bool Smoke)
{
    public const string Usage =
        "Usage: dotnet run scripts/installer.cs -- --rid <win-x64|linux-x64> --archive <the package's archive or its folder> [--output <folder>] [--smoke]";

    public static InstallerOptions? Parse(IReadOnlyList<string> args)
    {
        var options = new InstallerOptions(string.Empty, string.Empty, null, Smoke: false);
        string[] rest = [.. args];

        while (rest.Length > 0)
        {
            (options, rest) = rest switch
            {
                ["--rid", var rid, .. var next] => (options with { Rid = rid }, next),
                ["--archive", var archive, .. var next] => (options with { Archive = archive }, next),
                ["--output", var output, .. var next] => (options with { Output = output }, next),
                ["--smoke", .. var next] => (options with { Smoke = true }, next),
                _ => (options with { Rid = string.Empty }, []),
            };
        }

        return Targets.IsKnown(options.Rid) && options.Archive.Length > 0 ? options : null;
    }
}

internal enum TargetOs
{
    Windows,
    Linux,
}

internal sealed record Target(string Rid, TargetOs Os)
{
    public string Channel => Os == TargetOs.Windows ? "win" : "linux";

    public string ExecutableName => Os == TargetOs.Windows ? "Avala.exe" : "Avala";

    public string ArchiveExtension => Os == TargetOs.Windows ? ".zip" : ".tar.gz";

    public IEnumerable<string> Directive =>
        (Os == TargetOs.Windows) == OperatingSystem.IsWindows() ? [] : [$"[{Channel}]"];
}

internal static class Targets
{
    private static readonly Target[] Known =
    [
        new("win-x64", TargetOs.Windows),
        new("linux-x64", TargetOs.Linux),
    ];

    public static bool IsKnown(string rid) => Known.Any(target => target.Rid == rid);

    public static Target Of(string rid) => Known.Single(target => target.Rid == rid);
}

internal static class Archives
{
    public static string Find(string path, Target target) =>
        Directory.Exists(path)
            ? Directory.EnumerateFiles(path, $"avala-*-{target.Rid}{target.ArchiveExtension}").SingleOrDefault()
                ?? throw new InvalidOperationException($"{path} holds no single avala-<version>-{target.Rid}{target.ArchiveExtension}.")
            : File.Exists(path) ? path : throw new FileNotFoundException($"{path} does not exist.");

    public static string VersionOf(string archive, Target target)
    {
        var name = Path.GetFileName(archive);
        var suffix = $"-{target.Rid}{target.ArchiveExtension}";

        return name.StartsWith("avala-", StringComparison.Ordinal) && name.EndsWith(suffix, StringComparison.Ordinal)
            ? name["avala-".Length..^suffix.Length]
            : throw new InvalidOperationException($"{name} is not named avala-<version>{suffix}.");
    }

    public static async Task<string> ExtractAsync(string archive, string folder)
    {
        Directory.CreateDirectory(folder);

        if (archive.EndsWith(".zip", StringComparison.Ordinal))
        {
            await ZipFile.ExtractToDirectoryAsync(archive, folder);
        }
        else
        {
            await using var file = File.OpenRead(archive);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gzip, folder, overwriteFiles: false);
        }

        return Directory.EnumerateDirectories(folder).Single();
    }
}

internal static class Velopack
{
    public const string PackId = "Avala.Desktop";

    public const string Repository = "https://github.com/rick-dev-creator/avala";

    public static async Task DownloadPreviousAsync(string root, Target target, string version, string releases)
    {
        await Processes.RunAsync(
            "dotnet",
            [
                "vpk", "download", "github",
                "--repoUrl", Repository,
                "--channel", target.Channel,
                .. version.Contains('-', StringComparison.Ordinal) ? ["--pre"] : Array.Empty<string>(),
                "--outputDir", releases,
            ],
            root,
            allowFailure: true);

        Console.WriteLine(Directory.EnumerateFiles(releases, "*-full.nupkg").Any()
            ? "Downloaded the latest published release, so this one carries a delta from it."
            : "No published release with a Velopack package was found, so this one carries no delta.");
    }

    public static Task PackAsync(string root, Target target, string version, string packDir, string releases) =>
        Processes.RunAsync(
            "dotnet",
            [
                "vpk", .. target.Directive, "pack",
                "--packId", PackId,
                "--packVersion", version,
                "--packDir", packDir,
                "--mainExe", target.ExecutableName,
                "--packTitle", "Avala",
                "--packAuthors", "rick-dev-creator",
                "--channel", target.Channel,
                "--outputDir", releases,
                .. target.Os == TargetOs.Windows
                    ? ["--icon", Path.Combine(root, "docs", "assets", "brand", "avala.ico")]
                    : new[] { "--icon", Path.Combine(root, "docs", "assets", "brand", "avala-icon-256.png"), "--categories", "Development" },
                .. Notes(root, version),
            ],
            root);

    private static IEnumerable<string> Notes(string root, string version) =>
        Path.Combine(root, "docs", "releases", $"v{version}.md") is var notes && File.Exists(notes) ? ["--releaseNotes", notes] : [];
}

internal sealed record Made(IReadOnlyList<string> Offered, IReadOnlyList<string> Feed, string? AppImage);

internal static class Feed
{
    public static async Task<Made> PublishAsync(string releases, Target target, string version, string output)
    {
        var built = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(releases, $"assets.{target.Channel}.json")))!.AsArray()
            .Select(asset => (Name: (string)asset!["RelativeFileName"]!, Type: (string)asset["Type"]!))
            .ToList();
        var name = $"avala-{version}-{target.Rid}";
        var offered = new List<string>();
        var feed = new List<string>();
        string? appImage = null;

        foreach (var (file, type) in built)
        {
            var published = (type, target.Os) switch
            {
                ("Installer", _) => $"{name}-setup.exe",
                ("Portable", TargetOs.Windows) => $"{name}-portable.zip",
                ("Portable", _) => $"{name}.AppImage",
                _ => file,
            };
            var destination = Path.Combine(output, published);
            File.Copy(Path.Combine(releases, file), destination, overwrite: true);
            (type is "Installer" or "Portable" ? offered : feed).Add(destination);
            appImage = published.EndsWith(".AppImage", StringComparison.Ordinal) ? destination : appImage;
        }

        var index = Path.Combine(output, $"releases.{target.Channel}.json");
        await File.WriteAllTextAsync(index, await ThisVersionOnlyAsync(Path.Combine(releases, $"releases.{target.Channel}.json"), version));
        feed.Add(index);

        return new Made(offered, feed, appImage);
    }

    private static async Task<string> ThisVersionOnlyAsync(string index, string version)
    {
        var feed = JsonNode.Parse(await File.ReadAllTextAsync(index))!.AsObject();
        var assets = feed["Assets"]!.AsArray();

        foreach (var other in assets.Where(asset => (string)asset!["Version"]! != version).ToList())
        {
            assets.Remove(other);
        }

        return feed.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}

internal static class Smoke
{
    public static async Task RunAsync(string appImage, string version)
    {
        if (!OperatingSystem.IsLinux())
        {
            Console.WriteLine("Skipping the smoke test: the AppImage does not run on this machine.");
            return;
        }

        File.SetUnixFileMode(appImage, File.GetUnixFileMode(appImage) | UnixFileMode.UserExecute);
        var reported = await Processes.RunAsync(appImage, ["--version"], Path.GetDirectoryName(appImage)!, extractAndRun: true);

        if (!reported.Output.StartsWith($"Avala {version} (", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"--version reported \"{reported.Output}\" instead of Avala {version}.");
        }

        var smoke = await Processes.RunAsync(appImage, ["--smoke"], Path.GetDirectoryName(appImage)!, extractAndRun: true);
        Console.WriteLine($"{Path.GetFileName(appImage)}: {reported.Output}. {smoke.Output}");
    }
}

internal static class Checksums
{
    public static async Task<string> WriteAsync(string file)
    {
        await using var stream = File.OpenRead(file);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
        await File.WriteAllTextAsync($"{file}.sha256", $"{hash}  {Path.GetFileName(file)}\n");

        return hash;
    }
}

internal static class Report
{
    public static async Task LineAsync(string file, string detail)
    {
        var size = $"{new FileInfo(file).Length / 1024.0 / 1024.0:F1} MB";
        Console.WriteLine($"{Path.GetFileName(file)}: {size}, {detail}");

        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
        {
            await File.AppendAllTextAsync(summary, $"| {Path.GetFileName(file)} | {size} | {detail} |\n");
        }
    }
}

internal static class Folders
{
    public static void Recreate(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);
    }
}

internal sealed record ProcessResult(bool Succeeded, string Output);

internal static class Processes
{
    public static async Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> arguments, string directory, bool allowFailure = false, bool extractAndRun = false)
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

        if (extractAndRun)
        {
            info.Environment["APPIMAGE_EXTRACT_AND_RUN"] = "1";
            info.Environment.Remove("AVALA_DATA_PATH");
            info.Environment.Remove("AVALA_PLUGINS_PATH");
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"{file} could not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var result = new ProcessResult(process.ExitCode == 0, (await output).Trim());

        return result.Succeeded || allowFailure
            ? result
            : throw new InvalidOperationException($"{Path.GetFileName(file)} {string.Join(' ', arguments)} exited with {process.ExitCode}: {result.Output}\n{(await error).Trim()}");
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
