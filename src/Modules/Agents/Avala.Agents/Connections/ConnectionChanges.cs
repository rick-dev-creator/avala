using Avala.Agents.Contracts.Connections;
using Avala.Sdk;

namespace Avala.Agents.Connections;

internal interface IConnectionChange
{
    Result<ConnectionDeclarations, ConnectionError> ApplyTo(ConnectionDeclarations declarations);
}

internal sealed record DefaultChange(Option<ConnectionName> Connection) : IConnectionChange
{
    public Result<ConnectionDeclarations, ConnectionError> ApplyTo(ConnectionDeclarations declarations) =>
        declarations with { Fixed = Connection };
}

internal sealed record DeclarationChange(Option<ConnectionName> Replacing, ConnectionName Name, string Provider, Option<CredentialDeclaration> Credential) : IConnectionChange
{
    public Result<ConnectionDeclarations, ConnectionError> ApplyTo(ConnectionDeclarations declarations)
    {
        var replaced = Replacing.Bind(name => declarations.Connections.FirstOrDefault(connection => connection.Name == name).ToOption());

        if (Replacing.IsSome && replaced.IsNone)
        {
            return ConnectionError.UnknownConnection;
        }

        if (declarations.Connections.Any(connection => connection.Name == Name && Replacing != connection.Name))
        {
            return ConnectionError.DuplicateName;
        }

        var declared = replaced.Match(found => found, () => new ConnectionDeclaration(Name, Provider)) with { Name = Name, Provider = Provider, Credential = Credential };

        return declarations with
        {
            Connections = replaced.IsNone
                ? [.. declarations.Connections, declared]
                : [.. declarations.Connections.Select(connection => Replacing == connection.Name ? declared : connection)],
            Fixed = Replacing.IsSome && declarations.Fixed == Replacing ? Name : declarations.Fixed,
        };
    }
}

internal sealed record RemovalChange(ConnectionName Name) : IConnectionChange
{
    public Result<ConnectionDeclarations, ConnectionError> ApplyTo(ConnectionDeclarations declarations) =>
        !declarations.Connections.Any(connection => connection.Name == Name) ? ConnectionError.UnknownConnection
        : declarations.Fixed == Name ? ConnectionError.RemovesTheDefault
        : declarations with { Connections = [.. declarations.Connections.Where(connection => connection.Name != Name)] };
}
