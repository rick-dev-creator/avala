using Avala.Sdk;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace Avala.Components.UI.Markdown;

public sealed record CodeSpan(string Text, string Role);

public static class CodeColors
{
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(200);
    private static readonly RegistryOptions Options = new(ThemeName.DarkPlus);
    private static readonly Registry Grammars = new(Options);
    private static readonly Dictionary<string, Option<IGrammar>> Loaded = new(StringComparer.OrdinalIgnoreCase);

    private static readonly (string Scope, string Role)[] Roles =
    [
        ("comment", "comment"),
        ("punctuation.definition.comment", "comment"),
        ("string", "string"),
        ("markup.inline.raw", "string"),
        ("constant.numeric", "number"),
        ("constant.language", "number"),
        ("constant.character", "number"),
        ("entity.name.function", "function"),
        ("support.function", "function"),
        ("meta.function-call", "function"),
        ("entity.name.type", "type"),
        ("entity.name.class", "type"),
        ("entity.other.inherited-class", "type"),
        ("support.type", "type"),
        ("support.class", "type"),
        ("entity.other.attribute-name", "type"),
        ("entity.name.tag", "keyword"),
        ("storage", "keyword"),
        ("keyword.operator", string.Empty),
        ("keyword", "keyword"),
        ("markup.heading", "keyword"),
        ("markup.inserted", "string"),
        ("markup.deleted", "deleted"),
    ];

    public static IReadOnlyList<IReadOnlyList<CodeSpan>> Lines(string code, string language)
    {
        var lines = code.Split('\n');

        return Grammar(language).Match<IReadOnlyList<IReadOnlyList<CodeSpan>>>(
            grammar => Colored(grammar, lines),
            () => [.. lines.Select(line => (IReadOnlyList<CodeSpan>)[new CodeSpan(line, string.Empty)])]);
    }

    private static List<IReadOnlyList<CodeSpan>> Colored(IGrammar grammar, string[] lines)
    {
        var colored = new List<IReadOnlyList<CodeSpan>>(lines.Length);
        IStateStack? state = null;

        foreach (var line in lines)
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
                    Add(spans, new CodeSpan(line[start..end], Role(token.Scopes)));
                }
            }

            colored.Add(spans.Count == 0 ? [new CodeSpan(line, string.Empty)] : spans);
        }

        return colored;
    }

    private static void Add(List<CodeSpan> spans, CodeSpan span)
    {
        if (spans.Count > 0 && spans[^1].Role == span.Role)
        {
            spans[^1] = spans[^1] with { Text = spans[^1].Text + span.Text };
        }
        else
        {
            spans.Add(span);
        }
    }

    private static string Role(List<string> scopes)
    {
        for (var index = scopes.Count - 1; index >= 0; index--)
        {
            foreach (var (scope, role) in Roles)
            {
                if (scopes[index].StartsWith(scope, StringComparison.Ordinal))
                {
                    return role;
                }
            }
        }

        return string.Empty;
    }

    private static Option<IGrammar> Grammar(string language)
    {
        var name = language.Trim();

        if (name.Length == 0)
        {
            return Option<IGrammar>.None;
        }

        if (!Loaded.TryGetValue(name, out var grammar))
        {
            grammar = Scope(name).Bind(scope => Grammars.LoadGrammar(scope).ToOption());
            Loaded[name] = grammar;
        }

        return grammar;
    }

    private static Option<string> Scope(string name)
    {
        var extension = "." + name;
        var language = Options.GetAvailableLanguages().FirstOrDefault(known =>
            string.Equals(known.Id, name, StringComparison.OrdinalIgnoreCase)
            || known.Aliases?.Any(alias => string.Equals(alias, name, StringComparison.OrdinalIgnoreCase)) == true
            || known.Extensions?.Any(found => string.Equals(found, extension, StringComparison.OrdinalIgnoreCase)) == true);

        return language is null ? Option<string>.None : Options.GetScopeByLanguageId(language.Id).ToOption();
    }
}
