using Avala.Components.Status;

namespace Avala.Components.Tests.Status;

public sealed class MoodsTests
{
    [Theory]
    [InlineData(StatusKind.Working, Mood.Focused)]
    [InlineData(StatusKind.Checking, Mood.Focused)]
    [InlineData(StatusKind.NeedsYou, Mood.Asking)]
    [InlineData(StatusKind.Held, Mood.Asking)]
    [InlineData(StatusKind.Failed, Mood.Worried)]
    [InlineData(StatusKind.ReadyForReview, Mood.Pleased)]
    [InlineData(StatusKind.Done, Mood.Pleased)]
    public void AvasMoodFollowsTheJob(StatusKind status, Mood mood) =>
        Assert.Equal(mood, Moods.Of(status));
}
