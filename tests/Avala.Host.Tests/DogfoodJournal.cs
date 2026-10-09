using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Host.Composition;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Runtime.Diagnostics;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Presentation;
using Avala.Sdk.Processes;
using Avala.Sdk.Regions;
using Avala.Shell;
using Avala.Shell.Regions;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal sealed class DogfoodJournal : IAsyncDisposable
{
    private readonly Channel<(string File, string Line)> lines = Channel.CreateUnbounded<(string, string)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task writing;
    private readonly string folder;
    private int progressed;
    private int sampled;

    public DogfoodJournal(string folder)
    {
        this.folder = Directory.CreateDirectory(folder).FullName;
        writing = WriteAsync();
    }

    public Task NoteAsync(string text) => AddAsync("notes.log", $"{DateTime.Now:HH:mm:ss} {text}");

    public Task DecideAsync(string text) => Task.WhenAll(AddAsync("decisions.log", $"{DateTime.Now:HH:mm:ss} {text}"), NoteAsync($"DECISION {text}"));

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

        var text = happened.ToString() ?? string.Empty;
        text = text.Length > 1500 ? string.Concat(text.AsSpan(0, 1500), "…") : text;

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
            await File.AppendAllTextAsync(Path.Combine(folder, file), line + Environment.NewLine);
        }
    }
}

internal sealed class DogfoodRound(int round)
{
    public int Number => round;

    public int Verifications { get; set; }

    public bool Replied { get; set; }

    public bool Running { get; set; }
}
