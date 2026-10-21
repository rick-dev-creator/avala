using Avala.Agents.Contracts.Connections;
using Avala.Sdk;
using Avala.Workbench.Spending;

namespace Avala.Workbench.Overview;

internal sealed record HarnessMark(string Name, string Instance, string Logo, string Color)
{
    public static HarnessMark None { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty);

    public bool HasLogo => Logo.Length > 0;

    public bool HasBrand => Color.Length > 0;

    public bool IsKnown => Name.Length > 0;

    public bool HasInstance => Instance.Length > 0;

    public static HarnessMark Of(JobSpend spend, Option<ConnectionName> connection) =>
        spend.Session.Match(
            seen => new HarnessMark(seen.Provider.Name, connection.Match(name => name.Value, () => seen.Connection.Value), seen.Provider.Logo, seen.Provider.Color),
            () => None with { Instance = connection.Match(name => name.Value, () => string.Empty) });
}
