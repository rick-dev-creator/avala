using Avala.Runtime.Containment;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Runtime.Tests.Processes;

public sealed class CommandTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AProgramThatSucceedsGivesItsStandardOutputAsync()
    {
        var command = Workloads.Command("env", "PATH");

        var output = await Commands.OutputAsync(command.FileName, [.. command.ArgumentList], Cancellation);

        Assert.Equal(Option<string>.Some($"{Environment.GetEnvironmentVariable("PATH")}{Environment.NewLine}"), output);
    }

    [Fact]
    public async Task AProgramThatFailsGivesNoOutputAsync()
    {
        var command = Workloads.Command("unknown", "0");

        Assert.Equal(Option<string>.None, await Commands.OutputAsync(command.FileName, [.. command.ArgumentList], Cancellation));
    }

    [Fact]
    public async Task AProgramThatIsNotInstalledGivesNoOutputAsync() =>
        Assert.Equal(Option<string>.None, await Commands.OutputAsync("avala-no-such-executable", [], Cancellation));
}
