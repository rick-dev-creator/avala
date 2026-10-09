using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Observability.Contracts;

public sealed record UsageRecorded(SessionId Session, Option<JobId> Job) : IIntegrationEvent;
