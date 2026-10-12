using Avala.Runtime.Appearance;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Runtime.Tests.Appearance;

public sealed class OnboardingFileTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AFinishedTourIsWrittenNextToTheAppearanceAndReadBackByTheNextStartAsync()
    {
        await using var data = new TemporaryFolder();
        var finished = await new OnboardingFile(new AvalaPaths(data.Path)).FinishAsync(Cancellation);

        var restarted = new OnboardingFile(new AvalaPaths(data.Path));

        Assert.True(finished);
        Assert.True(await restarted.IsDoneAsync(Cancellation));
        Assert.Equal("{\n  \"onboarded\": true\n}\n", (await File.ReadAllTextAsync(Path.Combine(data.Path, OnboardingFile.FileName), Cancellation)).ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{ "onboarded": false }""")]
    [InlineData("""{ "onboarded": "yes" }""")]
    [InlineData("""[ true ]""")]
    [InlineData("not json")]
    public async Task WithoutAFinishedTourTheTourIsDueAsync(string? content)
    {
        await using var data = new TemporaryFolder();

        if (content is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(data.Path, OnboardingFile.FileName), content, Cancellation);
        }

        Assert.False(await new OnboardingFile(new AvalaPaths(data.Path)).IsDoneAsync(Cancellation));
    }
}
