using Avala.Agents.Contracts.Connections;
using Avala.Sdk;

namespace Avala.Workbench.Contracts.Presentation;

public sealed record DefaultConnectionChanged(DefaultMode Mode, Option<ConnectionName> Connection);
