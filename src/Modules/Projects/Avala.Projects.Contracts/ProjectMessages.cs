using Avala.Jobs.Contracts;
using Avala.Sdk.Events;

namespace Avala.Projects.Contracts;

public sealed record ProjectsRestored(ProjectMap Map) : IIntegrationEvent;

public sealed record ProjectCreated(ProjectRecord Project) : IIntegrationEvent;

public sealed record ProjectRenamed(ProjectRecord Project) : IIntegrationEvent;

public sealed record ProjectDeleted(ProjectId Project) : IIntegrationEvent;

public sealed record ConversationMoved(JobId Job, ProjectId Project) : IIntegrationEvent;
