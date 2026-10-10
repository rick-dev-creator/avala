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

public sealed class DogfoodRuns(HeadlessUi ui, PublishedPlugins plugins)
{
    [Fact]
    public async Task AnAppIsVibeCodedRestartedWhileCheckedReviewedAndApprovedThroughTheWorkbenchAsync()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(Gate) == "1", $"Set {Gate}=1 to dogfood Avala with real Claude Code.");
        Assert.SkipUnless(HeadlessApp.Renders, "Set AVALA_HEADLESS_RENDER=1 so the dogfood run can take screenshots.");
        var name = Resumed.Length > 0 ? Resumed : $"notes-{DateTime.Now:yyyyMMdd-HHmmss}";
        var folder = Directory.CreateDirectory(Path.Combine(Root, name)).FullName;
        var screens = Directory.CreateDirectory(Path.Combine(DogfoodSettings.Screens, name)).FullName;
        await using var journal = new DogfoodJournal(Path.Combine(folder, "report"));

        using var dogfood = new DogfoodDriver(plugins, journal, folder, screens);

        await ui.RunAsync(dogfood.RunAsync, Cancellation);
    }

}
