using Avala.Budgets.BudgetFiles;
using Avala.Budgets.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Budgets.Tests.BudgetFiles;

public sealed class BudgetFileFormatTests
{
    public static TheoryData<string, BudgetError?> Files => new()
    {
        { """{ "costPerJob": { "USD": 5 } }""", null },
        { "not json", BudgetError.Malformed },
        { $$"""{ "costPerJob": { "USD": 5 }, "note": "{{new string('x', BudgetFileReader.MaximumBytes)}}" }""", BudgetError.TooLarge },
    };

    [Theory]
    [MemberData(nameof(Files))]
    public void AnEditedBudgetFileIsAcceptedOrRejectedForBudgetsWithItsError(string content, BudgetError? error)
    {
        var format = new BudgetFileFormat();

        Assert.Equal(".avala/budget.json", format.Path);
        Assert.Equal(
            error is { } rejected ? Option<RuleFileRejection>.Some(new RuleFileRejection("Budgets", rejected)) : Option<RuleFileRejection>.None,
            format.Rejection(content));
    }
}
