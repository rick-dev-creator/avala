using System.Text;
using Avala.Budgets.Caps;
using Avala.Budgets.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Budgets.BudgetFiles;

internal sealed class BudgetFileFormat : IRuleFileFormat
{
    public string Path => Breaches.BudgetFile;

    public Option<Enum> Rejection(string content) =>
        Encoding.UTF8.GetByteCount(content) > BudgetFileReader.MaximumBytes
            ? Option<Enum>.Some(BudgetError.TooLarge)
            : BudgetFileParser.Parse(content).Match(_ => Option<Enum>.None, error => Option<Enum>.Some(error));
}
