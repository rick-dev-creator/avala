using System.Text;
using System.Text.RegularExpressions;

namespace Avala.Components.Privacy;

public static partial class PersonalMask
{
    public const string Hidden = "•••••";

    public const string HiddenDomain = "••••";

    public const string Home = "~";

    private static readonly string CurrentHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('/', '\\');

    private static readonly Regex CurrentHomePrefix =
        new($@"(?<![\w/\\.~-]){Regex.Escape(CurrentHome)}(?=$|[/\\\s""'`)\]])", RegexOptions.None, TimeSpan.FromSeconds(1));

    public static string Mask(PersonalKind kind, string value) =>
        value.Length == 0 ? value : kind switch
        {
            PersonalKind.Email => Email(value),
            PersonalKind.Login => Login(value),
            PersonalKind.AccountName => Name(value),
            PersonalKind.Path => Path(value),
            PersonalKind.OrganizationId => $"{value[..Math.Min(4, value.Length)]}{Hidden}",
            _ => value,
        };

    public static PersonalKind KindOfAccount(string label) =>
        EmailAddress().Match(label) is { Success: true } found && found.Length == label.Length ? PersonalKind.Email
        : Guid.TryParse(label, out _) ? PersonalKind.OrganizationId
        : PersonalKind.AccountName;

    public static string Scrub(string text) =>
        HomeFolder().Replace(EmailAddress().Replace(ScrubCurrentHome(text), found => Email(found.Value)), Home);

    private static string Email(string value)
    {
        var at = value.LastIndexOf('@');

        if (at <= 0 || at == value.Length - 1)
        {
            return Name(value);
        }

        var domain = value[(at + 1)..];
        var dot = domain.LastIndexOf('.');
        var ending = dot > 0 ? domain[dot..] : string.Empty;

        return $"{value[0]}{Hidden}@{domain[0]}{HiddenDomain}{ending}";
    }

    private static string Login(string value) => $"{value[..Math.Min(2, Math.Max(1, value.Length - 1))]}{Hidden}";

    private static string Name(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Word));

    private static string Word(string word)
    {
        var first = word.TakeWhile(character => !char.IsLetterOrDigit(character)).Count();

        if (first == word.Length)
        {
            return word;
        }

        var last = word.Length - word.Reverse().TakeWhile(character => !char.IsLetterOrDigit(character)).Count();

        return new StringBuilder(word[..(first + 1)]).Append(Hidden).Append(word[last..]).ToString();
    }

    private static string Path(string value) => HomeFolder().Replace(ScrubCurrentHome(value), Home, 1);

    private static string ScrubCurrentHome(string text) =>
        CurrentHome.Length > 1 ? CurrentHomePrefix.Replace(text, Home) : text;

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+")]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"(?<![\w/\\.~-])(?:/home/[^/\s""'`]+|/Users/[^/\s""'`]+|[A-Za-z]:\\Users\\[^\\\s""'`]+)(?=$|[/\\\s""'`)\]])")]
    private static partial Regex HomeFolder();
}
