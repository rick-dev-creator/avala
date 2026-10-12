using Avala.Agents.Contracts.Events;
using Avala.Sdk;

namespace Avala.Permissions.Policies;

internal static class Subjects
{
    public static Option<string> Of(ItemKind kind, string target) => kind switch
    {
        _ when target.Length == 0 => Option<string>.None,
        ItemKind.Command => target.IndexOf(' ', StringComparison.Ordinal) is var space and >= 0 ? target[..space] : target,
        ItemKind.Web => Host(target),
        _ => target,
    };

    private static Option<string> Host(string target) =>
        Uri.TryCreate(target, UriKind.Absolute, out var address) && address.Scheme is "http" or "https" && address.Host.Length > 0
            ? address.Host
            : Option<string>.None;
}
