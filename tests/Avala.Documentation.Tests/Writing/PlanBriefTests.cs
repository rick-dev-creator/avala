using Avala.Documentation.Writing;
using Avala.Sdk;

namespace Avala.Documentation.Tests.Writing;

public sealed class PlanBriefTests
{
    [Fact]
    public async Task AChildOfADocumentedJobIsNeverAskedForATaskListAsync()
    {
        var documented = Documented.With();
        var (parent, _) = await documented.RunningAsync();
        var (child, _) = await documented.RunningAsync("Write the notes", parent);

        var briefed = (await documented.Brief().BriefAsync(parent, Documented.Cancellation), await documented.Brief().BriefAsync(child, Documented.Cancellation));

        Assert.Equal((Option<string>.Some(PlanBrief.Text), Option<string>.None), briefed);
    }
}
