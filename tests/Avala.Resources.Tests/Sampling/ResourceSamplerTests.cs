using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.Sampling;
using Avala.Resources.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Resources.Tests.Sampling;

public sealed class ResourceSamplerTests
{
    private static CancellationToken Cancellation => Resourced.Cancellation;

    [Fact]
    public async Task ResourcesAreSampledOncePerIntervalAndDisksOnlyWhenTheirIntervalElapsedAsync()
    {
        var settings = ResourceSettingsParser.Defaults with { Sampling = TimeSpan.FromSeconds(5), DiskSampling = TimeSpan.FromSeconds(10) };
        await using var resources = new Resourced(settings);
        _ = await resources.OpenAsync(JobId.New(), Resourced.Home("one"));
        await using var sampler = new ResourceSampler(resources.Settings, resources.Taker, resources.Clock, NullLogger<ResourceSampler>.Instance);
        await sampler.RunAsync(Cancellation);

        var measures = new List<int>();

        for (var tick = 1; tick <= 3; tick++)
        {
            await AdvanceUntilSampledAsync(resources, TimeSpan.FromSeconds(5), tick);
            measures.Add(resources.Folders.Measured);
        }

        Assert.Equal([1, 1, 2], measures);
        Assert.Equal(3, resources.Bus.Published.OfType<ResourcesSampled>().Count());
    }

    private static async Task AdvanceUntilSampledAsync(Resourced resources, TimeSpan interval, int samples)
    {
        resources.Clock.Advance(interval);
        await resources.Bus.WaitForAsync<ResourcesSampled>(_ => resources.Bus.Published.OfType<ResourcesSampled>().Count() == samples, Cancellation);
    }
}
