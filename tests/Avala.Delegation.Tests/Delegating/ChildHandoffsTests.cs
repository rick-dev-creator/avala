using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Delegation.Delegating;
using Avala.Delegation.Policy;
using Avala.Delegation.Records;
using Avala.Delegation.Tests.Records;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Tests.Delegating;

public sealed class ChildHandoffsTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("child", "work, personal")]
    [InlineData("child of a section without connections", "")]
    [InlineData("job that is no child", "")]
    public async Task ADelegatedChildMayBeHandedOffAmongItsDelegationConnectionsAndNoOtherJobIsGivenTermsAsync(string situation, string connections)
    {
        var book = new DelegationBook(new InMemoryDelegations());
        var child = JobId.New();
        await book.KeepAsync(new DelegationRecord(SessionId.New(), new ItemId("delegate"), "Build the calculator", DateTimeOffset.UnixEpoch) { Parent = JobId.New(), Child = child }, Cancellation);
        var section = situation == "child of a section without connections" ? Desk.Declared with { Connections = [] } : Desk.Declared;

        var terms = await new ChildHandoffs(book, new FixedRules(Option<DelegationRules>.Some(section)))
            .OfAsync(situation == "job that is no child" ? JobId.New() : child, Desk.Worktree, Cancellation);

        Assert.Equal(connections, terms.Match(found => string.Join(", ", found.Connections.Select(connection => connection.Value)), () => string.Empty));
    }
}
