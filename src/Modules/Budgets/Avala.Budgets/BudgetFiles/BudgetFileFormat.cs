using System.Text;
using Avala.Budgets.Caps;
using Avala.Budgets.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Budgets.BudgetFiles;

internal sealed class BudgetFileFormat : IRuleFileFormat
{
    public string Path => Breaches.BudgetFile;

    public Option<RuleFileRejection> Rejection(string content) =>
        Encoding.UTF8.GetByteCount(content) > BudgetFileReader.MaximumBytes
            ? Rejected(BudgetError.TooLarge)
            : BudgetFileParser.Parse(content).Match(_ => Option<RuleFileRejection>.None, Rejected);

    private static Option<RuleFileRejection> Rejected(BudgetError error) => RuleFileRejection.Of("Budgets", error);
}
