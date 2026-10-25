using Ardalis.Result;
using Mediator;

namespace Avala.Server.Activity.Contracts.Ingestion;

internal sealed record IngestEvents(Guid Instance, string InstanceName, IReadOnlyList<IIncomingEvent> Events) : ICommand<Result<IngestionReceipt>>;
