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

    public static IReadOnlyList<Scenario> All { get; } = [Previews];
}
