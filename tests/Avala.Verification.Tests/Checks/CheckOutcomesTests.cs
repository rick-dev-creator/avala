using Avala.Verification.Checks;

namespace Avala.Verification.Tests.Checks;

public sealed class CheckOutcomesTests
{
    [Fact]
    public void EvidenceKeepsOnlyTheMarkedTailOfALongOutputAndAShortOneWhole()
    {
        var check = new DeclaredCheck("tests", "dotnet", ["test"], TimeSpan.FromMinutes(1));
        var longOutput = string.Concat(new string('a', 10), new string('z', CheckOutcomes.TailLength), "\n\n");

        var evidence = check.Exited(1, longOutput, "short error\n", TimeSpan.FromSeconds(3));

        Assert.Equal("[...]" + new string('z', CheckOutcomes.TailLength), evidence.OutputTail);
        Assert.Equal("short error", evidence.ErrorTail);
    }
}
