using Avala.Agents.Contracts.Capabilities;

namespace Avala.Fixtures.Violating.Domain;

public sealed record MisplacedCapability(int Level) : ICapability;
