using Avala.Agents.Contracts.Connections;
using Avala.Workbench.Settings;

namespace Avala.Workbench.Tests.Settings;

public sealed class ConnectionPhrasesTests
{
    [Theory]
    [InlineData(ConnectionError.InvalidName, "A name is letters, digits, '-', '_' or '.', starts with a letter or digit, and is not auto.")]
    [InlineData(ConnectionError.DuplicateName, "Another connection already has that name.")]
    [InlineData(ConnectionError.UnknownProvider, "No harness on this machine provides that.")]
    [InlineData(ConnectionError.UnknownSource, "This machine has no such credential source.")]
    [InlineData(ConnectionError.MissingReference, "Say where the credential is.")]
    [InlineData(ConnectionError.UnknownConnection, "That connection is not declared in connections.json: only declared connections can be changed here.")]
    [InlineData(ConnectionError.RemovesTheDefault, "This is the default connection: choose another default first.")]
    [InlineData(ConnectionError.Unwritable, "connections.json could not be written.")]
    [InlineData(ConnectionError.Malformed, "connections.json is rejected (Malformed): open it to fix it.")]
    public void AnEditOfAConnectionTheMachineRefusedSaysWhy(ConnectionError error, string phrase) =>
        Assert.Equal(phrase, ConnectionPhrases.Refused(error));
}
