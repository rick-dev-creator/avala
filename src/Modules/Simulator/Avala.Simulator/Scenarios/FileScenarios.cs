using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using static Avala.Simulator.Scenarios.ScenarioCatalog;

namespace Avala.Simulator.Scenarios;

internal static class FileScenarios
{
    public const string Orbits = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 240 140\"><ellipse cx=\"120\" cy=\"70\" rx=\"100\" ry=\"50\" fill=\"none\" stroke=\"#6b7280\"/><circle cx=\"120\" cy=\"70\" r=\"16\" fill=\"#f5b942\"/><circle cx=\"220\" cy=\"70\" r=\"6\" fill=\"#5b9be8\"/></svg>\n";

    public const string Page = "<!doctype html>\n<html lang=\"es\"><body><h1>Sistema solar</h1></body></html>\n";

    public static Scenario Previews { get; } = new("previews",
    [
        [
            Thought("A drawing and a page ", "show the orbits best."),
            new WriteFile(new ItemId("drawing"), "orbits.svg", Orbits),
            new WriteFile(new ItemId("page"), "sistema-solar.html", Page),
            Message("Drew orbits.svg ", "and wrote sistema-solar.html."),
            .. Bill(2_400, 380, 0.0180m, 0.22),
            new Finish(),
        ],
    ]);

    public static Scenario ScratchFiles { get; } = new("scratch-files",
    [
        [
            Thought("I will keep my notes ", "in the temporary folder."),
            new WriteFile(new ItemId("notes"), "$TMPDIR/notes.txt", "Scratch notes\n"),
            new WriteFile(new ItemId("escape"), "../escape.txt", "Outside the worktree\n"),
            Message("Kept my notes ", "and wrote beside the worktree."),
            .. Bill(1_600, 140, 0.0080m, 0.12),
            new Finish(),
        ],
    ]);

    public static Scenario SiblingReads { get; } = new("sibling-reads",
    [
        [
            Thought("The other repository ", "shows how it is done."),
            new UseTool(new ItemId("sibling"), ItemKind.Other, "Read ../sibling/README.md", "../sibling/README.md", "../sibling/README.md", "# Sibling\n", AsksPermission: true) { Reads = "../sibling/README.md" },
            new UseTool(new ItemId("secret"), ItemKind.Other, "Read ../../secrets.env", "../../secrets.env", "../../secrets.env", "TOKEN=1\n", AsksPermission: true) { Reads = "../../secrets.env" },
            Message("Read the sibling repository."),
            .. Bill(1_200, 90, 0.0060m, 0.08),
            new Finish(),
        ],
    ]);

    public static Scenario FolderRequest { get; } = new("request-folder",
    [
        [
            Thought("The shared contracts live ", "beside this worktree."),
            new CallTools([new CallTool(new ItemId("ask-folder"), "request_folder", """{ "path": "..", "access": "edit", "reason": "Update the shared contracts the API depends on" }""")]),
            new WriteFile(new ItemId("contract"), "../contracts.txt", "Orders v2\n"),
            Message("Updated the shared contracts."),
            .. Bill(1_500, 120, 0.0070m, 0.10),
            new Finish(),
        ],
    ]);

    public static IReadOnlyList<Scenario> All { get; } = [Previews, ScratchFiles, SiblingReads, FolderRequest];
}
