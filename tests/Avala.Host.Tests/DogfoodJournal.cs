using System.Globalization;
using System.Threading.Channels;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Sdk.Events;

namespace Avala.Host.Tests;

internal sealed class DogfoodJournal : IAsyncDisposable
{
    private readonly Channel<(string File, string Line)> lines = Channel.CreateUnbounded<(string, string)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task writing;
    private int progressed;
    private int sampled;

    public DogfoodJournal(string folder)
    {
        Folder = Directory.CreateDirectory(folder).FullName;
        writing = WriteAsync();
    }

    public string Folder { get; }

    public Task NoteAsync(string text) => AddAsync("notes.log", $"{DateTime.Now:HH:mm:ss} {text}");

    public Task DecideAsync(string text) => Task.WhenAll(AddAsync("decisions.log", $"{DateTime.Now:HH:mm:ss} {text}"), NoteAsync($"DECISION {text}"));

    public Task LogAsync(string file, string text) => AddAsync(file, $"{DateTime.Now:HH:mm:ss} {text.ReplaceLineEndings(" ⏎ ")}");

    public Task EventAsync(TimeSpan at, IIntegrationEvent happened)
    {
        switch (happened)
        {
            case AgentActivity { Event: ItemProgressed }:
                progressed++;
                return Task.CompletedTask;
        }

        if (happened.GetType().Name == "ResourcesSampled")
        {
            sampled++;
            return Task.CompletedTask;
        }

        var text = happened is AgentActivity activity ? $"AgentActivity {DogfoodJson.Show(activity.Event)}" : DogfoodJson.Show(happened);
        text = text.Length > 3000 ? string.Concat(text.AsSpan(0, 3000), "…") : text;

        return AddAsync("timeline.log", string.Create(CultureInfo.InvariantCulture, $"{at:hh\\:mm\\:ss\\.fff} [{progressed} deltas, {sampled} samples] {text.ReplaceLineEndings(" ⏎ ")}"));
    }

    public async ValueTask DisposeAsync()
    {
        lines.Writer.TryComplete();
        await writing;
    }

    private Task AddAsync(string file, string line)
    {
        lines.Writer.TryWrite((file, line));

        return Task.CompletedTask;
    }

    private async Task WriteAsync()
    {
        await foreach (var (file, line) in lines.Reader.ReadAllAsync())
        {
            await File.AppendAllTextAsync(Path.Combine(Folder, file), line + Environment.NewLine);
        }
    }
}
