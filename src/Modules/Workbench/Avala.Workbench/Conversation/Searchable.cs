namespace Avala.Workbench.Conversation;

internal enum FindMark
{
    None,
    Match,
    Current,
}

internal interface ISearchable
{
    string SearchText { get; }

    FindMark Found { get; set; }

    bool Unfold();
}
