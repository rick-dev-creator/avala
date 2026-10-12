using System.Globalization;
using Avala.Agents.Contracts.Sessions;
using static Avala.Simulator.Scenarios.ScenarioCatalog;

namespace Avala.Simulator.Scenarios;

internal static class AnimationScenarios
{
    private static readonly (string Name, int Orbit, double Size, string Color, int Seconds)[] Planets =
    [
        ("Mercury", 60, 3, "#b0b0b0", 4), ("Venus", 90, 6, "#e8c27a", 7), ("Earth", 125, 6.5, "#3b82f6", 10), ("Mars", 160, 5, "#ef4444", 19),
        ("Jupiter", 215, 14, "#d6a36b", 40), ("Saturn", 270, 12, "#e5cf8f", 70), ("Uranus", 320, 9, "#67e8f9", 120), ("Neptune", 365, 9, "#4f6df5", 160),
    ];

    public static Scenario AnimatedCanvas { get; } = new("animated-canvas",
    [
        [
            Thought("A chart that grows, with today still counting, shows the week best."),
            new Draw(new ItemId("chart"), "Jobs finished this week", "image/svg+xml",
            [
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 320 180\" font-size=\"11\">",
                "<line x1=\"40\" y1=\"150\" x2=\"300\" y2=\"150\" stroke=\"#6b7280\" stroke-opacity=\"0.5\"/>",
                "<rect x=\"55\" y=\"150\" width=\"40\" height=\"0\" rx=\"4\" fill=\"#6366f1\"><animate attributeName=\"height\" from=\"0\" to=\"90\" begin=\"0s\" dur=\"1.2s\" fill=\"freeze\" calcMode=\"spline\" keyTimes=\"0;1\" keySplines=\"0.2 0.8 0.2 1\"/><animate attributeName=\"y\" from=\"150\" to=\"60\" begin=\"0s\" dur=\"1.2s\" fill=\"freeze\" calcMode=\"spline\" keyTimes=\"0;1\" keySplines=\"0.2 0.8 0.2 1\"/></rect>",
                "<rect x=\"120\" y=\"150\" width=\"40\" height=\"0\" rx=\"4\" fill=\"#6366f1\"><animate attributeName=\"height\" from=\"0\" to=\"120\" begin=\"0.2s\" dur=\"1.2s\" fill=\"freeze\" calcMode=\"spline\" keyTimes=\"0;1\" keySplines=\"0.2 0.8 0.2 1\"/><animate attributeName=\"y\" from=\"150\" to=\"30\" begin=\"0.2s\" dur=\"1.2s\" fill=\"freeze\" calcMode=\"spline\" keyTimes=\"0;1\" keySplines=\"0.2 0.8 0.2 1\"/></rect>",
                "<rect x=\"185\" y=\"150\" width=\"40\" height=\"0\" rx=\"4\" fill=\"#6366f1\"><animate attributeName=\"height\" from=\"0\" to=\"60\" begin=\"0.4s\" dur=\"1.2s\" fill=\"freeze\" calcMode=\"spline\" keyTimes=\"0;1\" keySplines=\"0.2 0.8 0.2 1\"/><animate attributeName=\"y\" from=\"150\" to=\"90\" begin=\"0.4s\" dur=\"1.2s\" fill=\"freeze\" calcMode=\"spline\" keyTimes=\"0;1\" keySplines=\"0.2 0.8 0.2 1\"/></rect>",
                "<rect x=\"250\" y=\"150\" width=\"40\" height=\"0\" rx=\"4\" fill=\"#0ea5e9\"><animate attributeName=\"height\" from=\"0\" to=\"105\" begin=\"0.6s\" dur=\"1.2s\" fill=\"freeze\" calcMode=\"spline\" keyTimes=\"0;1\" keySplines=\"0.2 0.8 0.2 1\"/><animate attributeName=\"y\" from=\"150\" to=\"45\" begin=\"0.6s\" dur=\"1.2s\" fill=\"freeze\" calcMode=\"spline\" keyTimes=\"0;1\" keySplines=\"0.2 0.8 0.2 1\"/></rect>",
                "<circle cx=\"270\" cy=\"33\" r=\"4\" fill=\"#0ea5e9\"><animate attributeName=\"r\" values=\"3;6;3\" dur=\"1.6s\" repeatCount=\"indefinite\"/><animate attributeName=\"opacity\" values=\"1;0.3;1\" dur=\"1.6s\" repeatCount=\"indefinite\"/></circle>",
                "<g fill=\"#6b7280\" text-anchor=\"middle\"><text x=\"75\" y=\"166\">Mon</text><text x=\"140\" y=\"166\">Tue</text>",
                "<text x=\"205\" y=\"166\">Wed</text><text x=\"270\" y=\"166\">Thu</text></g>",
                "</svg>",
            ]),
            Message("The chart grows ", "to this week's finished jobs; today's dot keeps pulsing while it counts."),
            .. Bill(3_100, 700, 0.0310m, 0.35),
            new Finish(),
        ],
    ]);

    public static Scenario SolarSystem { get; } = new("solar-system",
    [
        [
            Thought("An orbiting diagram shows the planets best."),
            new Draw(new ItemId("solar"), "Animated solar system", "image/svg+xml", SolarChunks()),
            Message("Here is the solar system, ", "each planet orbiting at its own pace."),
            .. Bill(4_200, 1_900, 0.0420m, 0.40),
            new Finish(),
        ],
    ]);

    public static IReadOnlyList<Scenario> All { get; } = [AnimatedCanvas, SolarSystem];

    private static List<string> SolarChunks()
    {
        var stars = Enumerable.Range(0, 90).Select(star => string.Create(
            CultureInfo.InvariantCulture,
            $"<circle cx=\"{(star * 397 + 31) % 1000}\" cy=\"{(star * 211 + 17) % 800}\" r=\"{(star % 3 == 0 ? 1.4 : 0.8)}\" fill=\"#ffffff\" opacity=\"{(star % 2 == 0 ? 0.5 : 0.9)}\"/>"));
        var orbits = Planets.Select(planet => string.Create(CultureInfo.InvariantCulture, $"<circle cx=\"600\" cy=\"420\" r=\"{planet.Orbit}\" fill=\"none\" stroke=\"#ffffff\" stroke-opacity=\"0.12\"/>"));
        var bodies = Planets.Select(planet => string.Create(
            CultureInfo.InvariantCulture,
            $"<g><animateTransform attributeName=\"transform\" type=\"rotate\" from=\"0 600 420\" to=\"360 600 420\" dur=\"{planet.Seconds}s\" repeatCount=\"indefinite\"/><circle cx=\"{600 + planet.Orbit}\" cy=\"420\" r=\"{planet.Size}\" fill=\"{planet.Color}\"/></g>"));
        var legend = Planets.Select((planet, index) => string.Create(
            CultureInfo.InvariantCulture,
            $"<circle cx=\"44\" cy=\"{74 + (index * 26)}\" r=\"6\" fill=\"{planet.Color}\"/><text x=\"58\" y=\"{79 + (index * 26)}\">{planet.Name}</text>"));

        return
        [
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1000 800\" width=\"600\" height=\"480\">",
            "<defs><radialGradient id=\"glow\"><stop offset=\"0%\" stop-color=\"#fff7c2\"/><stop offset=\"45%\" stop-color=\"#ffb020\"/><stop offset=\"100%\" stop-color=\"#ff6a00\" stop-opacity=\"0\"/></radialGradient></defs>",
            "<rect width=\"1000\" height=\"800\" fill=\"#05060f\"/>",
            .. stars,
            .. orbits,
            "<circle cx=\"600\" cy=\"420\" r=\"60\" fill=\"url(#glow)\"/><circle cx=\"600\" cy=\"420\" r=\"30\" fill=\"#ffb020\"/>",
            .. bodies,
            "<rect x=\"20\" y=\"20\" width=\"170\" height=\"272\" rx=\"8\" fill=\"#0d1224\" stroke=\"#ffffff\" stroke-opacity=\"0.15\"/>",
            "<g font-family=\"sans-serif\" font-size=\"15\" fill=\"#e5e7eb\"><circle cx=\"44\" cy=\"48\" r=\"6\" fill=\"#ffb020\"/><text x=\"58\" y=\"53\">Sun</text>",
            .. legend,
            "</g></svg>",
        ];
    }
}
