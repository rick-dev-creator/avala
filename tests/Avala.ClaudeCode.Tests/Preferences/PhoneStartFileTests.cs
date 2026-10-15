using Avala.ClaudeCode.Preferences;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.ClaudeCode.Tests.Preferences;

public sealed class PhoneStartFileTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheSwitchIsKeptInClaudeCodesOwnFileAndReadBackAtStartupAsync()
    {
        using var folder = new TemporaryFolder();
        var paths = new AvalaPaths(folder.Path);

        Assert.True(await new PhoneStartFile(paths).ChooseAsync(on: true, Cancellation));
        var restarted = new PhoneStartFile(paths);
        var before = restarted.StartsOn;
        await restarted.RunAsync(Cancellation);

        Assert.False(before);
        Assert.True(restarted.StartsOn);
        Assert.Equal("""{"startJobsOnPhone":true}""", await File.ReadAllTextAsync(Path.Combine(folder.Path, PhoneStartFile.FileName), Cancellation));
    }

    [Fact]
    public async Task WithoutItsFileJobsDoNotStartOnThePhoneAsync()
    {
        using var folder = new TemporaryFolder();
        var file = new PhoneStartFile(new AvalaPaths(folder.Path));

        await file.RunAsync(Cancellation);

        Assert.False(file.StartsOn);
    }
}
