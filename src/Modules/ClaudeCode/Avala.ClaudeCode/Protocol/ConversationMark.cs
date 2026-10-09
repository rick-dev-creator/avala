using System.Globalization;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.ClaudeCode.Protocol;

internal readonly record struct ConversationMark(Guid Session, decimal Spent)
{
    private const char Separator = '/';

    public ResumeToken Token => new($"{Session:D}{Separator}{Spent.ToString(CultureInfo.InvariantCulture)}");

    public static Option<ConversationMark> Read(ResumeToken token)
    {
        var parts = token.Value.Split(Separator);

        return parts.Length == 2
            && Guid.TryParseExact(parts[0], "D", out var session)
            && decimal.TryParse(parts[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var spent)
                ? new ConversationMark(session, spent)
                : Option<ConversationMark>.None;
    }
}
