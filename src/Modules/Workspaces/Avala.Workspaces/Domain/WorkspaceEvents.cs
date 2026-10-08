using Avala.Sdk.Domain;
using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Domain;

internal sealed record WorkspaceReady(WorkspaceId Workspace, WorkspaceLocation Location, BranchName Branch) : IDomainEvent;

internal sealed record CheckpointRecorded(WorkspaceId Workspace, Checkpoint Checkpoint) : IDomainEvent;

internal sealed record WorkspaceFailed(WorkspaceId Workspace) : IDomainEvent;

internal sealed record WorkspaceRemoved(WorkspaceId Workspace) : IDomainEvent;
