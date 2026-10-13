using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

var options = AurOptions.Parse(args);

if (options is null)
{
    await Console.Error.WriteLineAsync(AurOptions.Usage);
    return 1;
}

var root = Repository.Root();
var archive = Archives.Find(Path.GetFullPath(options.Archive));
var version = Archives.VersionOf(archive);
var hash = await Archives.HashAsync(archive);
var template = await File.ReadAllTextAsync(Path.Combine(root, "packaging", "aur", "PKGBUILD"));
var pkgbuild = template
    .Replace("@PKGVER@", version.Replace('-', '_'), StringComparison.Ordinal)
    .Replace("@VERSION@", version, StringComparison.Ordinal)
    .Replace("@SHA256@", hash, StringComparison.Ordinal);
var srcinfo = SrcInfo.Of(pkgbuild);

if (options.Output is { } output)
{
    Directory.CreateDirectory(output);
    await File.WriteAllTextAsync(Path.Combine(output, $"avala-bin-{version}.PKGBUILD"), pkgbuild);
    await File.WriteAllTextAsync(Path.Combine(output, $"avala-bin-{version}.SRCINFO"), srcinfo);
}

if (options.Into is { } into)
{
    Directory.CreateDirectory(into);
    await File.WriteAllTextAsync(Path.Combine(into, "PKGBUILD"), pkgbuild);
    await File.WriteAllTextAsync(Path.Combine(into, ".SRCINFO"), srcinfo);
}

Console.WriteLine($"avala-bin {version.Replace('-', '_')}-1 from {Path.GetFileName(archive)}, sha256 {hash}");

return 0;

internal sealed record AurOptions(string Archive, string? Output, string? Into)
{
    public const string Usage =
        "Usage: dotnet run scripts/aur.cs -- --archive <avala-<version>-linux-x64.tar.gz or its folder> [--output <folder for release assets>] [--into <AUR clone>]";

    public static AurOptions? Parse(IReadOnlyList<string> args)
    {
        var options = new AurOptions(string.Empty, null, null);
        string[] rest = [.. args];

        while (rest.Length > 0)
        {
            (options, rest) = rest switch
            {
                ["--archive", var archive, .. var next] => (options with { Archive = archive }, next),
                ["--output", var output, .. var next] => (options with { Output = Path.GetFullPath(output) }, next),
                ["--into", var into, .. var next] => (options with { Into = Path.GetFullPath(into) }, next),
                _ => (options with { Archive = string.Empty }, []),
            };
        }

        return options.Archive.Length > 0 && (options.Output is not null || options.Into is not null) ? options : null;
    }
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

    public static async Task<string> HashAsync(string archive)
    {
        await using var file = File.OpenRead(archive);

        return Convert.ToHexStringLower(await SHA256.HashDataAsync(file));
    }

    [GeneratedRegex(@"^avala-(?<version>\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?)-linux-x64\.tar\.gz$")]
    private static partial Regex LinuxArchive();
}

internal static partial class SrcInfo
{
    private static readonly string[] Order =
        ["pkgdesc", "pkgver", "pkgrel", "url", "arch", "license", "depends", "optdepends", "provides", "conflicts", "options", "source", "sha256sums"];

    public static string Of(string pkgbuild)
    {
        var fields = pkgbuild.ReplaceLineEndings("\n").Split('\n')
            .Select(line => Field().Match(line))
            .Where(match => match.Success)
            .ToDictionary(match => match.Groups["name"].Value, match => Values(match.Groups["value"].Value), StringComparer.Ordinal);
        var name = fields["pkgname"].Single();
        var text = new StringBuilder($"pkgbase = {name}\n");

        foreach (var key in Order.Where(fields.ContainsKey))
        {
            foreach (var value in fields[key])
            {
                text.Append(CultureInfo.InvariantCulture, $"\t{key} = {value}\n");
            }
        }

        return text.Append(CultureInfo.InvariantCulture, $"\npkgname = {name}\n").ToString();
    }

    private static string[] Values(string value) =>
        value.StartsWith('(')
            ? [.. Quoted().Matches(value).Select(match => match.Groups["text"].Value)]
            : [value.Trim('\'', '"')];

    [GeneratedRegex(@"^(?<name>[a-z0-9_]+)=(?<value>.+)$")]
    private static partial Regex Field();

    [GeneratedRegex(@"'(?<text>[^']*)'")]
    private static partial Regex Quoted();
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
