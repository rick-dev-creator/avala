using Avala.Handoffs.Policy;

namespace Avala.Handoffs.Tests.Policy;

public sealed class UsageWindowsTests
{
    [Theory]
    [InlineData("5h", 5 * 60)]
    [InlineData("7d", 7 * 24 * 60)]
    [InlineData("30m", 30)]
    [InlineData("7d_opus", 7 * 24 * 60)]
    [InlineData("weekly", 5 * 60)]
    [InlineData("0h", 5 * 60)]
    [InlineData("", 5 * 60)]
    public void AWindowsLengthIsReadFromItsNameOrIsFiveHoursWhenTheNameSaysNone(string window, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), UsageWindows.LengthOf(window));
}
