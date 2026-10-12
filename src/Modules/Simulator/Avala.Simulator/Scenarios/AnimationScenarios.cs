using Avala.Agents.Contracts.Sessions;
using static Avala.Simulator.Scenarios.ScenarioCatalog;

namespace Avala.Simulator.Scenarios;

internal static class AnimationScenarios
{
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

    public static IReadOnlyList<Scenario> All { get; } = [AnimatedCanvas];
}
