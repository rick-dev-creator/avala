using Avala.Sdk;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using EditorTheme = TextMateSharp.Themes.Theme;

namespace Avala.Components.UI.Markdown;

internal sealed class CodeTokens(string language)
{
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(200);
    private static readonly RegistryOptions Options = new(ThemeName.DarkPlus);
    private static readonly Registry Grammars = new(Options);
    private static readonly Dictionary<string, Option<IGrammar>> Loaded = new(StringComparer.OrdinalIgnoreCase);

    private static readonly EditorTheme Dark = EditorTheme.CreateFromRawTheme(Options.LoadTheme(ThemeName.DarkPlus), Options);
    private static readonly EditorTheme Light = EditorTheme.CreateFromRawTheme(Options.LoadTheme(ThemeName.LightPlus), Options);
    private static readonly Dictionary<string, CodeInk> Inks = new(StringComparer.Ordinal);

    private IStateStack? state;

    public IReadOnlyList<IReadOnlyList<CodeSpan>> Next(string[] lines) =>
        Grammar(language).Match<IReadOnlyList<IReadOnlyList<CodeSpan>>>(
            grammar => [.. lines.Select(line => Line(grammar, line, ref state))],
            () => [.. lines.Select(line => (IReadOnlyList<CodeSpan>)[new CodeSpan(line, CodeInk.Plain)])]);

    private static List<CodeSpan> Line(IGrammar grammar, string line, ref IStateStack? state)
    {
        var result = grammar.TokenizeLine(line, state, Budget);
        state = result.RuleStack;
        var spans = new List<CodeSpan>();

        foreach (var token in result.Tokens)
        {
            var start = Math.Min(token.StartIndex, line.Length);
            var end = Math.Min(token.EndIndex, line.Length);

            if (end > start)
            {
                Add(spans, new CodeSpan(line[start..end], Ink(token.Scopes)));
            }
        }

        return spans.Count == 0 ? [new CodeSpan(line, CodeInk.Plain)] : spans;
    }

    private static void Add(List<CodeSpan> spans, CodeSpan span)
    {
        if (spans.Count > 0 && spans[^1].Ink == span.Ink)
        {
            spans[^1] = spans[^1] with { Text = spans[^1].Text + span.Text };
        }
        else
        {
            spans.Add(span);
        }
    }

    private static CodeInk Ink(List<string> scopes)
    {
        var key = string.Join(' ', scopes);

        if (!Inks.TryGetValue(key, out var ink))
        {
            ink = new CodeInk(Color(Dark, scopes), Color(Light, scopes));
            Inks[key] = ink;
        }

        return ink;
    }

    private static uint Color(EditorTheme theme, List<string> scopes)
    {
        var rule = theme.Match(scopes).FirstOrDefault(found => found.foreground > 0);

        return rule is null ? 0 : uint.Parse(theme.GetColor(rule.foreground).TrimStart('#')[..6], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture) | 0xFF000000;
    }

    private static Option<IGrammar> Grammar(string language)
    {
        if (language.Length == 0)
        {
            return Option<IGrammar>.None;
        }

        if (!Loaded.TryGetValue(language, out var grammar))
        {
            grammar = Scope(language).Bind(scope => Grammars.LoadGrammar(scope).ToOption());
            Loaded[language] = grammar;
        }

        return grammar;
    }

    public static string Extension(string name) =>
        Known(name.Trim()).Bind(language => (language.Extensions?.FirstOrDefault(found => found.StartsWith('.')) ?? string.Empty).ToOption()).Match(
            found => found.Length > 0 ? found : ".txt",
            () => ".txt");

    private static Option<string> Scope(string name) =>
        Known(name).Bind(language => Options.GetScopeByLanguageId(language.Id).ToOption());

    private static Option<Language> Known(string name)
    {
        var extension = "." + name;
        var language = name.Length == 0 ? null : Options.GetAvailableLanguages().FirstOrDefault(known =>
            string.Equals(known.Id, name, StringComparison.OrdinalIgnoreCase)
            || known.Aliases?.Any(alias => string.Equals(alias, name, StringComparison.OrdinalIgnoreCase)) == true
            || known.Extensions?.Any(found => string.Equals(found, extension, StringComparison.OrdinalIgnoreCase)) == true);

        return language is null ? Option<Language>.None : Option<Language>.Some(language);
    }
}
