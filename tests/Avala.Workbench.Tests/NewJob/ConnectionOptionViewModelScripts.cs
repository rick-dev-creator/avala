using Avala.Testing;
using Avala.Workbench.NewJob;

namespace Avala.Workbench.Tests.NewJob;

public sealed class ConnectionOptionViewModelScripts
{
    [Fact]
    public void AnOptionShowsTheReadingItIsGivenAndWhetherItIsNearItsLimit() =>
        ViewModelScript.Given(new ConnectionOptionViewModel("claude"))
            .When(option => option.Show("99% of the 7-day window used", nearLimit: true))
            .ThenNotified(nameof(ConnectionOptionViewModel.Reading), nameof(ConnectionOptionViewModel.IsNearLimit))
            .Then(option => Assert.Equal(("claude", "99% of the 7-day window used", true), (option.Name, option.Reading, option.IsNearLimit)));
}
