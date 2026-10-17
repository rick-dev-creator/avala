using System.Globalization;
using Avala.Projects.Contracts;

namespace Avala.Workbench.Sidebar;

internal static class ProjectPhrases
{
    public const string NewProject = "New project…";

    public static string Done(int count) => string.Create(CultureInfo.InvariantCulture, $"{count} done");

    public static string Refusal(ProjectRejection rejection) => rejection switch
    {
        ProjectRejection.NameEmpty => "A project needs a name",
        ProjectRejection.NameTooLong => $"A project's name has at most {ProjectMap.LongestName} characters",
        ProjectRejection.NameTaken => "Another project already has this name",
        ProjectRejection.NotEmpty => "Move its conversations to another project first",
        ProjectRejection.FollowsItsParent => "A sub-agent's conversation moves with its parent",
        ProjectRejection.UnknownJob => "This conversation is no longer known",
        ProjectRejection.StillWorking => "It is working again; archive it once it stops",
        ProjectRejection.NotArchived => "This conversation is no longer archived",
        _ => "This project no longer exists",
    };
}
