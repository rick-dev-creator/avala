using Avala.Agents.Contracts.Capabilities;

namespace Avala.Fixtures.Compliant.Contracts;

public sealed record TracksOrders(string Carrier) : ICapability;
