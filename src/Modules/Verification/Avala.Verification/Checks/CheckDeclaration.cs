using System.Text.Json;
using Avala.Sdk;

namespace Avala.Verification.Checks;

internal static class CheckDeclaration
{
    public const string RelativePath = ".avala/checks.json";

    private const double LongestTimeoutSeconds = 86_400;

    private static readonly JsonDocumentOptions Lenient = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static TimeSpan DefaultTimeout { get; } = TimeSpan.FromMinutes(10);

    public static Result<IReadOnlyList<DeclaredCheck>, VerificationError> Parse(string declaration)
    {
        try
        {
            using var document = JsonDocument.Parse(declaration, Lenient);

            return Checks(document.RootElement);
        }
        catch (JsonException)
        {
            return VerificationError.MalformedDeclaration;
        }
    }

    private static Result<IReadOnlyList<DeclaredCheck>, VerificationError> Checks(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("checks", out var checks)
            || checks.ValueKind != JsonValueKind.Array)
        {
            return VerificationError.MalformedDeclaration;
        }

        var declared = new List<DeclaredCheck>();

        foreach (var element in checks.EnumerateArray())
        {
            if (!Check(element).TryGetValue(out var check, out var error))
            {
                return error;
            }

            declared.Add(check);
        }

        return Result<IReadOnlyList<DeclaredCheck>, VerificationError>.Success(declared);
    }

    private static Result<DeclaredCheck, VerificationError> Check(JsonElement element) =>
        element.ValueKind != JsonValueKind.Object
            ? VerificationError.MalformedDeclaration
            : Command(element).Bind(command => Arguments(element).Bind(arguments => Timeout(element).Bind(timeout =>
                Text(element, "name").Map(name => Named(new DeclaredCheck(string.Empty, command, arguments, timeout), name)))));

    private static DeclaredCheck Named(DeclaredCheck check, Option<string> name) =>
        check with { Name = name.Match(given => string.IsNullOrWhiteSpace(given) ? check.CommandLine : given, () => check.CommandLine) };

    private static Result<string, VerificationError> Command(JsonElement element) =>
        Text(element, "command").Bind(command => command.Match<Result<string, VerificationError>>(
            text => string.IsNullOrWhiteSpace(text) ? VerificationError.MissingCommand : text,
            () => VerificationError.MissingCommand));

    private static Result<IReadOnlyList<string>, VerificationError> Arguments(JsonElement element)
    {
        if (!Present(element, "arguments", out var arguments))
        {
            return Result<IReadOnlyList<string>, VerificationError>.Success([]);
        }

        return arguments.ValueKind == JsonValueKind.Array
            && arguments.EnumerateArray().All(argument => argument.ValueKind == JsonValueKind.String)
            ? Result<IReadOnlyList<string>, VerificationError>.Success([.. arguments.EnumerateArray().Select(argument => argument.GetString() ?? string.Empty)])
            : VerificationError.MalformedDeclaration;
    }

    private static Result<TimeSpan, VerificationError> Timeout(JsonElement element)
    {
        if (!Present(element, "timeoutSeconds", out var timeout))
        {
            return DefaultTimeout;
        }

        return timeout.ValueKind == JsonValueKind.Number
            && timeout.TryGetDouble(out var seconds)
            && seconds is > 0 and <= LongestTimeoutSeconds
            ? TimeSpan.FromSeconds(seconds)
            : VerificationError.InvalidTimeout;
    }

    private static Result<Option<string>, VerificationError> Text(JsonElement element, string property)
    {
        if (!Present(element, property, out var value))
        {
            return Option<string>.None;
        }

        return value.ValueKind == JsonValueKind.String
            ? Option<string>.Some(value.GetString() ?? string.Empty)
            : VerificationError.MalformedDeclaration;
    }

    private static bool Present(JsonElement element, string property, out JsonElement value) =>
        element.TryGetProperty(property, out value) && value.ValueKind != JsonValueKind.Null;
}
