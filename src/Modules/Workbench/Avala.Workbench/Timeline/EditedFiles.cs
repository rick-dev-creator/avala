using Avala.Agents.Contracts.Events;
using Avala.Sdk;

namespace Avala.Workbench.Timeline;

internal static class EditedFiles
{
    extension(ToolEntry tool)
    {
        public Option<string> EditedPath =>
            tool.Kind == ItemKind.FileEdit
            && tool.Title.Split(' ', 2) is ["Write" or "Edit", { Length: > 0 } path]
            && !path.EndsWith('…')
                ? path.Trim()
                : Option<string>.None;
    }
}
