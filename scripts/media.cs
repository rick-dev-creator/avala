using System.Diagnostics;
using System.Globalization;

if (args.Length is < 1 or > 2)
{
    await Console.Error.WriteLineAsync("Usage: dotnet run scripts/media.cs -- <feature-screens-folder> [<output-folder>]");
    return 1;
}

var screens = args[0];
var output = Directory.CreateDirectory(args.Length == 2 ? args[1] : Path.Combine("docs", "assets", "readme")).FullName;
var reels = Path.Combine(screens, "reels");

if (!Directory.Exists(reels))
{
    await Console.Error.WriteLineAsync($"No reels in {reels}. Capture them first with AVALA_HEADLESS_RENDER=1 and AVALA_FEATURE_SCREENS={screens}.");
    return 1;
}

var made = new List<string>();

foreach (var reel in Directory.GetDirectories(reels).Order(StringComparer.Ordinal))
{
    made.AddRange(await Media.EncodeAsync(reel, output));
}

foreach (var source in Media.Picked(screens))
{
    if (!File.Exists(source.Path))
    {
        await Console.Error.WriteLineAsync($"{source.Path} is missing. Capture every feature screen first.");
        return 1;
    }

    made.Add(await Ffmpeg.StillAsync(source.Path, Path.Combine(output, source.Name + ".png")));
}

return Media.Report(made) ? 0 : 1;

internal static class Media
{
    private const int FramesPerSecond = 12;
    private const int WebpWidth = 1200;
    private const int GifWidth = 960;
    private const int GifFramesPerSecond = 10;
    private const long WebpLimit = 2L * 1024 * 1024;
    private const long GifLimit = 5L * 1024 * 1024;

    private static readonly (string Shot, string Name)[] Shots =
    [
        ("28-triggers-forge-events", "triggers"),
        ("27-settings-notifications", "notifications"),
        ("onboarding-4-privacy", "privacy"),
    ];

    private static readonly string[] Themes = ["dark", "light"];

    public static IEnumerable<(string Path, string Name)> Picked(string screens) =>
        Shots.SelectMany(shot => Themes.Select(theme => (Path.Combine(screens, $"{shot.Shot}-{theme}.png"), $"{shot.Name}-{theme}")));

    public static async Task<IReadOnlyList<string>> EncodeAsync(string reel, string output)
    {
        var name = Path.GetFileName(reel);
        var frames = Directory.GetFiles(reel, "*.png").Order(StringComparer.Ordinal).ToList();

        if (frames.Count == 1)
        {
            return [await Ffmpeg.StillAsync(frames[0], Path.Combine(output, name + ".png"))];
        }

        var pattern = Path.Combine(reel, "%04d.png");
        var rate = FramesPerSecond.ToString(CultureInfo.InvariantCulture);
        var webp = await Ffmpeg.RunAsync(
            Path.Combine(output, name + ".webp"),
            ["-framerate", rate, "-i", pattern, "-vf", $"scale={WebpWidth}:-1:flags=lanczos", "-c:v", "libwebp_anim", "-lossless", "0", "-quality", "82", "-compression_level", "6", "-loop", "0"]);

        if (!name.EndsWith("-light", StringComparison.Ordinal))
        {
            return [webp];
        }

        var gif = await Ffmpeg.RunAsync(
            Path.Combine(output, name + ".gif"),
            ["-framerate", rate, "-i", pattern, "-vf", $"fps={GifFramesPerSecond},scale={GifWidth}:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=128:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle", "-loop", "0"]);

        return [webp, gif];
    }

    public static bool Report(IReadOnlyList<string> made)
    {
        var fits = true;

        foreach (var file in made)
        {
            var bytes = new FileInfo(file).Length;
            var over = bytes > Limit(file);
            fits &= !over;
            Console.WriteLine($"{Path.GetFileName(file),-28} {bytes / 1024.0,9:N0} KB{(over ? "  over its limit" : string.Empty)}");
        }

        return fits;
    }

    private static long Limit(string file) => Path.GetExtension(file) switch
    {
        ".webp" => WebpLimit,
        ".gif" => GifLimit,
        _ => long.MaxValue,
    };
}

internal static class Ffmpeg
{
    public static Task<string> StillAsync(string source, string target) =>
        RunAsync(target, ["-i", source, "-compression_level", "9", "-pred", "mixed"]);

    public static async Task<string> RunAsync(string target, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };

        foreach (var argument in (string[])["-v", "error", "-y", .. arguments, target])
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("ffmpeg could not start. Install ffmpeg with libwebp.");
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0
            ? target
            : throw new InvalidOperationException($"ffmpeg failed on {target}: {(await error).Trim()}");
    }
}
