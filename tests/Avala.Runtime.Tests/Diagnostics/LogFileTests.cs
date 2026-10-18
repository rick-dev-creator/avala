using Avala.Runtime.Diagnostics;
using Avala.Sdk;
using Avala.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Runtime.Tests.Diagnostics;

public sealed class LogFileTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 10, 9, 30, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly Action<ILogger, string, string, Exception?> Held =
        LoggerMessage.Define<string, string>(LogLevel.Warning, default, "Job {Job} is held as {Reason}");

    private static void Say(ILogger logger, LogLevel level, string message, Exception? exception = null) =>
        logger.Log(level, default, message, exception, (said, _) => said);

    private static async Task<IReadOnlyList<LogEvent>> EventsAsync(string folder) =>
        LogEvents.Parse(await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(folder)), Cancellation));

    [Fact]
    public async Task EveryLoggerMessageOfTheRuntimeReachesTheFileOfTheDayWithItsLevelCategoryAndExceptionAsync()
    {
        await using var data = new TemporaryFolder();
        var paths = new AvalaPaths(data.Path);
        var clock = new FakeTimeProvider(Morning);

        await using (var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(clock).AddRuntime(paths).BuildServiceProvider())
        {
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Avala.Jobs.Ledger.JobQueues");
            Say(logger, LogLevel.Error, "Work on job job-7 failed", new InvalidOperationException("the store is gone"));
            Say(logger, LogLevel.Debug, "Too chatty to keep");
        }

        var logged = Assert.Single(await EventsAsync(paths.Logs));

        Assert.Equal("avala-20261010-000.log", Path.GetFileName(Assert.Single(Directory.GetFiles(paths.Logs))));
        Assert.Equal((Morning, "Error", "Avala.Jobs.Ledger.JobQueues", "Work on job job-7 failed"), (logged.At, logged.Level, logged.Category, logged.Message));
        Assert.StartsWith("System.InvalidOperationException: the store is gone", logged.Exception, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMessageIsKeptAsACompactLogEventWithItsTemplateAndEachOfItsPropertiesAsync()
    {
        await using var data = new TemporaryFolder();

        await using (var log = new LogFile(data.Path, new FakeTimeProvider(Morning), LogRedaction.From(new Dictionary<string, string>()), LogFile.Limit))
        {
            Held(log.CreateLogger("Avala.Jobs.Holding.JobJournal"), "job-7", "Stalled", null);
        }

        var logged = Assert.Single(await EventsAsync(data.Path));

        Assert.Equal(
            ("Warning", "Job {Job} is held as {Reason}", "Job job-7 is held as Stalled", "job-7", "Stalled"),
            (logged.Level, logged.Template, logged.Message, logged.Property("Job"), logged.Property("Reason")));
    }

    [Fact]
    public async Task AMessageLoggedInsideAScopeCarriesThePropertiesOfEveryScopeAroundItAsync()
    {
        await using var data = new TemporaryFolder();
        var clock = new FakeTimeProvider(Morning);

        await using (var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(clock).AddRuntime(new AvalaPaths(data.Path)).BuildServiceProvider())
        {
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Avala.Jobs.Holding.JobJournal");

            using (logger.BeginScope(new Dictionary<string, object?> { ["Job"] = "job-7" }))
            using (logger.BeginScope(new Dictionary<string, object?> { ["Session"] = "session-3" }))
            {
                Say(logger, LogLevel.Information, "Inside");
            }

            Say(logger, LogLevel.Information, "Outside");
        }

        var logged = await EventsAsync(new AvalaPaths(data.Path).Logs);

        Assert.Equal(
            [("Inside", "job-7", "session-3"), ("Outside", string.Empty, string.Empty)],
            logged.Select(entry => (entry.Message, entry.Property("Job"), entry.Property("Session"))));
    }

    [Fact]
    public async Task SecretsNeverReachTheFileAsync()
    {
        await using var data = new TemporaryFolder();
        var redaction = LogRedaction.From(new Dictionary<string, string>
        {
            ["WORK_API_KEY"] = "work-key-1234567890",
            ["GITHUB_TOKEN"] = "ghp_abcdefghijklmnop",
            ["PATH"] = "/usr/bin:/bin",
            ["SHORT_SECRET"] = "abc",
        });

        await using (var log = new LogFile(data.Path, new FakeTimeProvider(Morning), redaction, LogFile.Limit))
        {
            Say(log.CreateLogger("Avala.ClaudeCode.Cli.ProcessCli"), LogLevel.Warning,
                "env work-key-1234567890 and ghp_abcdefghijklmnop, key sk-ant-api03-Zx_9-q, header Authorization: Bearer eyJhbGciOi.payload, path /usr/bin:/bin, abc");
        }

        var text = Assert.Single(await EventsAsync(data.Path)).Message;

        Assert.Contains("env [redacted] and [redacted], key [redacted], header Authorization: Bearer [redacted], path /usr/bin:/bin, abc", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFullFileStartsAnotherAndOnlyTheNewestAreKeptAsync()
    {
        await using var data = new TemporaryFolder();
        var clock = new FakeTimeProvider(Morning);

        await using (var log = new LogFile(data.Path, clock, LogRedaction.From(new Dictionary<string, string>()), 200))
        {
            var logger = log.CreateLogger("Avala");

            for (var line = 0; line < LogFile.Kept + 5; line++)
            {
                Say(logger, LogLevel.Information, $"Line {line} of a long run, long enough to fill a small file on its own and more");
            }
        }

        var kept = Directory.GetFiles(data.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(LogFile.Kept, kept.Count);
        Assert.Contains("Line 14 of", await File.ReadAllTextAsync(Path.Combine(data.Path, kept[^1]!), Cancellation), StringComparison.Ordinal);
        Assert.Contains("Line 5 of", await File.ReadAllTextAsync(Path.Combine(data.Path, kept[0]!), Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANewDayStartsANewFileAsync()
    {
        await using var data = new TemporaryFolder();
        var clock = new FakeTimeProvider(Morning);

        await using (var log = new LogFile(data.Path, clock, LogRedaction.From(new Dictionary<string, string>()), LogFile.Limit))
        {
            var logger = log.CreateLogger("Avala");
            Say(logger, LogLevel.Information, "Today");
            clock.Advance(TimeSpan.FromDays(1));
            Say(logger, LogLevel.Information, "Tomorrow");
        }

        Assert.Equal(
            ["avala-20261010-000.log", "avala-20261011-000.log"],
            Directory.GetFiles(data.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }
}
