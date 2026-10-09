using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

var options = PackageOptions.Parse(args);

if (options is null)
{
    await Console.Error.WriteLineAsync(PackageOptions.Usage);
    return 1;
}

var root = Repository.Root();
var version = await Versions.ResolveAsync(options.Version, root);
var target = Targets.Of(options.Rid);
var work = Path.Combine(root, "artifacts", "package", options.Rid);
var output = Path.GetFullPath(options.Output ?? Path.Combine(root, "artifacts", "packages"));

Console.WriteLine($"Packaging Avala {version.Text} for {target.Rid}.");
Folders.Recreate(work);
Directory.CreateDirectory(output);

var stage = Path.Combine(work, "publish");
await Build.PublishHostAsync(root, target, version, stage);
await Build.PluginsAsync(root, version, Path.Combine(stage, "plugins"));
Layout.Trim(stage, target);
await Layout.ShareAsync(stage);
var executable = Layout.RenameExecutable(stage, target);
var bundle = target.Os switch
{
    TargetOs.MacOs => await MacBundle.CreateAsync(root, stage, work, version),
    TargetOs.Linux => await LinuxFolder.CreateAsync(root, stage, work, version),
    _ => WindowsFolder.Create(stage, work),
};
var launched = Path.Combine(bundle.ExecutableFolder, Path.GetFileName(executable));

if (options.Sign)
{
    await Signing.SignAsync(target, bundle, launched);
}

if (options.Smoke)
{
    await Smoke.RunAsync(target, launched, version);
}

var archive = await Archives.CreateAsync(target, bundle, output, version);
var hash = await Archives.WriteChecksumAsync(archive);

Console.WriteLine($"{Path.GetFileName(archive)}: {new FileInfo(archive).Length / 1024.0 / 1024.0:F1} MB, sha256 {hash}");

if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
{
    await File.AppendAllTextAsync(summary, $"| {Path.GetFileName(archive)} | {new FileInfo(archive).Length / 1024.0 / 1024.0:F1} MB | `{hash}` |\n");
}

return 0;

internal sealed record PackageOptions(string Rid, string? Version, string? Output, bool Smoke, bool Sign)
{
    public const string Usage =
        "Usage: dotnet run scripts/package.cs -- --rid <win-x64|linux-x64|osx-arm64|osx-x64> [--version <x.y.z[-pre]>] [--output <folder>] [--smoke] [--sign]";

    public static PackageOptions? Parse(IReadOnlyList<string> args)
    {
        var options = new PackageOptions(string.Empty, null, null, Smoke: false, Sign: false);
        string[] rest = [.. args];

        while (rest.Length > 0)
        {
            (options, rest) = rest switch
            {
                ["--rid", var rid, .. var next] => (options with { Rid = rid }, next),
                ["--version", var version, .. var next] => (options with { Version = version }, next),
                ["--output", var output, .. var next] => (options with { Output = output }, next),
                ["--smoke", .. var next] => (options with { Smoke = true }, next),
                ["--sign", .. var next] => (options with { Sign = true }, next),
                _ => (options with { Rid = string.Empty }, []),
            };
        }

        return Targets.IsKnown(options.Rid) ? options : null;
    }
}

internal enum TargetOs
{
    Windows,
    Linux,
    MacOs,
}

internal sealed record Target(string Rid, TargetOs Os)
{
    public string ExecutableName => Os == TargetOs.Windows ? "Avala.exe" : "Avala";

    public string HostExecutableName => Os == TargetOs.Windows ? "Avala.Host.exe" : "Avala.Host";

    public bool Keeps(string nativeFolder) =>
        nativeFolder == Rid || Os switch
        {
            TargetOs.Windows => nativeFolder == "win",
            TargetOs.Linux => nativeFolder is "linux" or "unix",
            _ => nativeFolder is "osx" or "unix",
        };

    public bool RunsHere =>
        Rid == $"{(OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux")}-{System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";
}

internal static class Targets
{
    private static readonly Target[] Known =
    [
        new("win-x64", TargetOs.Windows),
        new("linux-x64", TargetOs.Linux),
        new("osx-arm64", TargetOs.MacOs),
        new("osx-x64", TargetOs.MacOs),
    ];

    public static bool IsKnown(string rid) => Known.Any(target => target.Rid == rid);

    public static Target Of(string rid) => Known.Single(target => target.Rid == rid);
}

internal sealed partial record PackageVersion(string Text)
{
    public string Numeric => Text.Split('+', 2)[0].Split('-', 2)[0];

    public static PackageVersion? From(string? text) =>
        text?.Trim().TrimStart('v', 'V') is { Length: > 0 } bare && SemanticVersion().IsMatch(bare) ? new PackageVersion(bare) : null;

    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$")]
    private static partial Regex SemanticVersion();
}

internal static class Versions
{
    public const string Development = "0.1.0-dev";

    public static async Task<PackageVersion> ResolveAsync(string? requested, string root)
    {
        if (requested is not null)
        {
            return PackageVersion.From(requested) ?? throw new InvalidOperationException($"{requested} is not a semantic version such as 0.1.0 or 0.1.0-beta.1.");
        }

        if (Environment.GetEnvironmentVariable("GITHUB_REF_TYPE") == "tag" && PackageVersion.From(Environment.GetEnvironmentVariable("GITHUB_REF_NAME")) is { } tagged)
        {
            return tagged;
        }

        var described = await Processes.RunAsync("git", ["describe", "--tags", "--exact-match", "HEAD"], root, allowFailure: true);

        return (described.Succeeded ? PackageVersion.From(described.Output) : null) ?? new PackageVersion(Development);
    }
}

internal static class Build
{
    public static Task PublishHostAsync(string root, Target target, PackageVersion version, string stage) =>
        Processes.RunAsync(
            "dotnet",
            [
                "publish", Path.Combine(root, "src", "Avala.Host", "Avala.Host.csproj"),
                "--configuration", "Release",
                "--runtime", target.Rid,
                "--self-contained", "true",
                "--output", stage,
                $"-p:Version={version.Text}",
                "-p:PublishReadyToRun=false",
                "-p:PublishSingleFile=false",
            ],
            root);

    public static async Task PluginsAsync(string root, PackageVersion version, string plugins)
    {
        foreach (var project in PluginProjects(root))
        {
            await Processes.RunAsync(
                "dotnet",
                [
                    "build", project,
                    "--configuration", "Release",
                    $"-p:Version={version.Text}",
                    $"-p:AvalaPluginsDirectory={plugins}{Path.DirectorySeparatorChar}",
                ],
                root);
        }
    }

    private static IEnumerable<string> PluginProjects(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "src", "Modules"), "*.csproj", SearchOption.AllDirectories)
            .Where(project => File.ReadLines(project).Any(line => line.Contains("<AvalaPlugin>true</AvalaPlugin>", StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal);
}

internal static class Layout
{
    public static void Trim(string stage, Target target)
    {
        var shipped = Directory.EnumerateFiles(stage).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in Directory.EnumerateDirectories(Path.Combine(stage, "plugins")))
        {
            var runtimes = Path.Combine(plugin, "runtimes");

            if (Directory.Exists(runtimes))
            {
                foreach (var folder in Directory.EnumerateDirectories(runtimes).Where(folder => !target.Keeps(Path.GetFileName(folder))))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }

            foreach (var duplicate in Directory.EnumerateFiles(plugin).Where(file => shipped.Contains(Path.GetFileName(file)) && !IsPluginManifest(plugin, file)))
            {
                File.Delete(duplicate);
            }
        }
    }

    public static async Task ShareAsync(string stage)
    {
        var plugins = Path.Combine(stage, "plugins");
        var kept = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var plugin in Directory.EnumerateDirectories(plugins).Order(StringComparer.Ordinal))
        {
            foreach (var file in Directory.EnumerateFiles(plugin, "*", SearchOption.AllDirectories).Where(file => !IsPluginManifest(plugin, file)).Order(StringComparer.Ordinal))
            {
                var key = $"{Path.GetRelativePath(plugin, file)}:{await HashAsync(file)}";

                if (kept.ContainsKey(key))
                {
                    File.Delete(file);
                }
                else
                {
                    kept[key] = file;
                }
            }
        }
    }

    private static async Task<string> HashAsync(string file)
    {
        await using var stream = File.OpenRead(file);

        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    public static string RenameExecutable(string stage, Target target)
    {
        var host = Path.Combine(stage, target.HostExecutableName);
        var renamed = Path.Combine(stage, target.ExecutableName);
        File.Move(host, renamed);

        return renamed;
    }

    private static bool IsPluginManifest(string plugin, string file) =>
        Path.GetFileNameWithoutExtension(file).StartsWith(Path.GetFileName(plugin), StringComparison.Ordinal);
}

internal sealed record Bundle(string Root, string ExecutableFolder);

internal static class WindowsFolder
{
    public static Bundle Create(string stage, string work)
    {
        var folder = Path.Combine(work, "Avala");
        Directory.Move(stage, folder);

        return new Bundle(folder, folder);
    }
}

internal static class LinuxFolder
{
    public static async Task<Bundle> CreateAsync(string root, string stage, string work, PackageVersion version)
    {
        var folder = Path.Combine(work, $"avala-{version.Text}");
        Directory.Move(stage, folder);
        File.Copy(Path.Combine(root, "docs", "assets", "brand", "avala-icon-256.png"), Path.Combine(folder, "avala.png"));
        await File.WriteAllTextAsync(Path.Combine(folder, "avala.desktop"), $"""
            [Desktop Entry]
            Type=Application
            Name=Avala
            Comment=Agents you can trust without watching
            Exec=Avala
            Icon=avala
            Terminal=false
            Categories=Development;
            X-Avala-Version={version.Text}

            """);

        return new Bundle(folder, folder);
    }
}

internal static class MacBundle
{
    public const string Identifier = "io.github.rick-dev-creator.avala";

    public static async Task<Bundle> CreateAsync(string root, string stage, string work, PackageVersion version)
    {
        var app = Path.Combine(work, "Avala.app");
        var contents = Path.Combine(app, "Contents");
        var macOs = Path.Combine(contents, "MacOS");
        var resources = Path.Combine(contents, "Resources");
        Directory.CreateDirectory(contents);
        Directory.Move(stage, macOs);
        Directory.CreateDirectory(resources);
        await File.WriteAllBytesAsync(Path.Combine(resources, "avala.icns"), await Icns.FromBrandAsync(Path.Combine(root, "docs", "assets", "brand")));
        await File.WriteAllTextAsync(Path.Combine(contents, "Info.plist"), InfoPlist(version));

        return new Bundle(app, macOs);
    }

    private static string InfoPlist(PackageVersion version) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>CFBundleName</key>
          <string>Avala</string>
          <key>CFBundleDisplayName</key>
          <string>Avala</string>
          <key>CFBundleIdentifier</key>
          <string>{Identifier}</string>
          <key>CFBundleExecutable</key>
          <string>Avala</string>
          <key>CFBundleIconFile</key>
          <string>avala.icns</string>
          <key>CFBundlePackageType</key>
          <string>APPL</string>
          <key>CFBundleShortVersionString</key>
          <string>{version.Numeric}</string>
          <key>CFBundleVersion</key>
          <string>{version.Numeric}</string>
          <key>CFBundleInfoDictionaryVersion</key>
          <string>6.0</string>
          <key>LSMinimumSystemVersion</key>
          <string>13.0</string>
          <key>LSApplicationCategoryType</key>
          <string>public.app-category.developer-tools</string>
          <key>NSHighResolutionCapable</key>
          <true/>
          <key>NSPrincipalClass</key>
          <string>NSApplication</string>
          <key>NSHumanReadableCopyright</key>
          <string>MIT License</string>
        </dict>
        </plist>

        """;
}

internal static class Icns
{
    private static readonly (string Type, int Size)[] Entries =
    [
        ("icp4", 16),
        ("icp5", 32),
        ("icp6", 64),
        ("ic07", 128),
        ("ic08", 256),
        ("ic09", 512),
        ("ic10", 1024),
        ("ic11", 32),
        ("ic12", 64),
        ("ic13", 256),
        ("ic14", 512),
    ];

    public static async Task<byte[]> FromBrandAsync(string brand)
    {
        var images = new List<(string Type, byte[] Png)>();

        foreach (var (type, size) in Entries)
        {
            images.Add((type, await File.ReadAllBytesAsync(Path.Combine(brand, $"avala-icon-{size}.png"))));
        }

        return Write(images);
    }

    public static byte[] Write(IReadOnlyList<(string Type, byte[] Png)> images)
    {
        using var icns = new MemoryStream();
        using var writer = new BinaryWriter(icns);
        writer.Write("icns"u8);
        writer.Write(BigEndian(8 + images.Sum(image => 8 + image.Png.Length)));

        foreach (var (type, png) in images)
        {
            writer.Write(Encoding.ASCII.GetBytes(type));
            writer.Write(BigEndian(8 + png.Length));
            writer.Write(png);
        }

        writer.Flush();

        return icns.ToArray();
    }

    private static byte[] BigEndian(int value)
    {
        var bytes = BitConverter.GetBytes(value);

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return bytes;
    }
}

internal static class Smoke
{
    public static async Task RunAsync(Target target, string executable, PackageVersion version)
    {
        if (!target.RunsHere)
        {
            Console.WriteLine($"Skipping the smoke test: {target.Rid} does not run on this machine.");
            return;
        }

        var reported = await Processes.RunAsync(executable, ["--version"], Path.GetDirectoryName(executable)!);

        if (!reported.Output.StartsWith($"Avala {version.Text} (", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"--version reported \"{reported.Output}\" instead of Avala {version.Text}.");
        }

        var smoke = await Processes.RunAsync(executable, ["--smoke"], Path.GetDirectoryName(executable)!, isolated: true);
        Console.WriteLine($"{reported.Output}. {smoke.Output}");
    }
}

internal static class Archives
{
    public static async Task<string> CreateAsync(Target target, Bundle bundle, string output, PackageVersion version)
    {
        var name = $"avala-{version.Text}-{target.Rid}";

        if (target.Os == TargetOs.Linux)
        {
            var tarball = Path.Combine(output, $"{name}.tar.gz");
            File.Delete(tarball);
            await using var file = File.Create(tarball);
            await using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
            await TarFile.CreateFromDirectoryAsync(bundle.Root, gzip, includeBaseDirectory: true);

            return tarball;
        }

        var zip = Path.Combine(output, $"{name}.zip");
        File.Delete(zip);
        await ZipFile.CreateFromDirectoryAsync(bundle.Root, zip, CompressionLevel.SmallestSize, includeBaseDirectory: true);

        return zip;
    }

    public static async Task<string> WriteChecksumAsync(string archive)
    {
        await using var file = File.OpenRead(archive);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(file));
        await File.WriteAllTextAsync($"{archive}.sha256", $"{hash}  {Path.GetFileName(archive)}\n");

        return hash;
    }
}

internal static class Signing
{
    private const string Entitlements = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>com.apple.security.cs.allow-jit</key>
          <true/>
          <key>com.apple.security.cs.allow-unsigned-executable-memory</key>
          <true/>
          <key>com.apple.security.cs.disable-library-validation</key>
          <true/>
        </dict>
        </plist>

        """;

    public static Task SignAsync(Target target, Bundle bundle, string executable) => target.Os switch
    {
        TargetOs.Windows => SignWindowsAsync(bundle, executable),
        TargetOs.MacOs => SignMacAsync(bundle),
        _ => Task.CompletedTask,
    };

    private static async Task SignWindowsAsync(Bundle bundle, string executable)
    {
        var certificate = Path.Combine(Path.GetTempPath(), $"avala-{Guid.NewGuid():N}.pfx");
        await File.WriteAllBytesAsync(certificate, Convert.FromBase64String(Secret("AVALA_WINDOWS_CERTIFICATE")));

        try
        {
            var files = Directory.EnumerateFiles(bundle.Root, "Avala*.dll", SearchOption.AllDirectories).Prepend(executable);
            await Processes.RunAsync(
                SignTool(),
                [
                    "sign", "/f", certificate, "/p", Secret("AVALA_WINDOWS_CERTIFICATE_PASSWORD"),
                    "/fd", "SHA256", "/tr", "https://timestamp.digicert.com", "/td", "SHA256",
                    .. files,
                ],
                bundle.Root);
        }
        finally
        {
            File.Delete(certificate);
        }
    }

    private static async Task SignMacAsync(Bundle bundle)
    {
        var keychain = Path.Combine(Path.GetTempPath(), "avala-signing.keychain-db");
        var keychainPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var certificate = Path.Combine(Path.GetTempPath(), $"avala-{Guid.NewGuid():N}.p12");
        var entitlements = Path.Combine(Path.GetTempPath(), "avala-entitlements.plist");
        await File.WriteAllBytesAsync(certificate, Convert.FromBase64String(Secret("AVALA_MACOS_CERTIFICATE")));
        await File.WriteAllTextAsync(entitlements, Entitlements);
        var work = Path.GetDirectoryName(bundle.Root)!;

        try
        {
            await Processes.RunAsync("security", ["create-keychain", "-p", keychainPassword, keychain], work);
            await Processes.RunAsync("security", ["unlock-keychain", "-p", keychainPassword, keychain], work);
            await Processes.RunAsync("security", ["import", certificate, "-k", keychain, "-P", Secret("AVALA_MACOS_CERTIFICATE_PASSWORD"), "-T", "/usr/bin/codesign"], work);
            await Processes.RunAsync("security", ["set-key-partition-list", "-S", "apple-tool:,apple:", "-s", "-k", keychainPassword, keychain], work);
            await Processes.RunAsync("security", ["list-keychains", "-d", "user", "-s", keychain], work);
            var identity = Secret("AVALA_MACOS_SIGNING_IDENTITY");
            var binaries = Directory.EnumerateFiles(bundle.ExecutableFolder, "*", SearchOption.AllDirectories)
                .Where(file => file.EndsWith(".dylib", StringComparison.Ordinal) || Path.GetFileName(file) == "createdump");

            foreach (var binary in binaries.Append(Path.Combine(bundle.ExecutableFolder, "Avala")).Append(bundle.Root))
            {
                await Processes.RunAsync("codesign", ["--force", "--timestamp", "--options", "runtime", "--entitlements", entitlements, "--keychain", keychain, "--sign", identity, binary], work);
            }

            var submission = Path.Combine(work, "notarize.zip");
            await Processes.RunAsync("ditto", ["-c", "-k", "--keepParent", bundle.Root, submission], work);
            await Processes.RunAsync(
                "xcrun",
                ["notarytool", "submit", submission, "--apple-id", Secret("AVALA_APPLE_ID"), "--team-id", Secret("AVALA_APPLE_TEAM_ID"), "--password", Secret("AVALA_APPLE_APP_PASSWORD"), "--wait"],
                work);
            await Processes.RunAsync("xcrun", ["stapler", "staple", bundle.Root], work);
            File.Delete(submission);
        }
        finally
        {
            File.Delete(certificate);
            await Processes.RunAsync("security", ["delete-keychain", keychain], work, allowFailure: true);
        }
    }

    private static string SignTool() =>
        Directory.EnumerateFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "bin"), "signtool.exe", SearchOption.AllDirectories)
            .Where(path => path.Contains("x64", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .LastOrDefault()
        ?? throw new InvalidOperationException("signtool.exe was not found in the Windows SDK.");

    private static string Secret(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Signing needs {name}, which is not set. See docs/release.md.");
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
    public static async Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> arguments, string directory, bool allowFailure = false, bool isolated = false)
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

        if (isolated)
        {
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
            : throw new InvalidOperationException($"{Path.GetFileName(file)} {Redacted(arguments)} exited with {process.ExitCode}: {result.Output}\n{(await error).Trim()}");
    }

    private static string Redacted(IReadOnlyList<string> arguments) =>
        string.Join(' ', arguments.Select((argument, index) => index > 0 && arguments[index - 1] is "-p" or "/p" or "-P" or "--password" ? "***" : argument));
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
