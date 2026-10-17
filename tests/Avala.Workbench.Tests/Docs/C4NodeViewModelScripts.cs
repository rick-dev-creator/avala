using Avala.Documentation.Contracts;
using Avala.Workbench.Docs;
using Avala.Workbench.Documenting;

namespace Avala.Workbench.Tests.Docs;

public sealed class C4NodeViewModelScripts
{
    [Fact]
    public void ANodeNamesItsElementAndFollowsItsStateInTheScene()
    {
        var node = new C4NodeViewModel(new C4Element("postgres", "Ledger DB", C4Shape.Database, false) { Technology = "Postgres", Boundary = "ledger" });
        var hidden = node.IsHidden;

        node.Show(new SceneNode(new C4Element("postgres", "Ledger DB", C4Shape.Database, false), NodeState.Removed) { Reached = true });

        Assert.Equal(("postgres", "ledger", "Ledger DB", "Database", "Postgres", true, true), (node.Key, node.Frame, node.Label, node.Kind, node.Technology, node.IsDatabase, hidden));
        Assert.Equal((false, true, false, true), (node.IsHidden, node.IsRemoved, node.IsNew, node.IsReached));
    }
}
