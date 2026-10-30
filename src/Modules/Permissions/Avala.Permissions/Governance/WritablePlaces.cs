using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Permissions.Governance;

internal sealed record WritablePlaces(Option<AvalaPaths> Data, IReadOnlyList<string> Temporary);
