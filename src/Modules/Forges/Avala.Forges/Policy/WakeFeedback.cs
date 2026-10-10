using System.Globalization;
using System.Text;
using Avala.Forges.Contracts;

namespace Avala.Forges.Policy;

internal static class WakeFeedback
{
    public const string Closing = "Avala pushes your work to the pull request once your turn passes its checks.";

    private const int Head = 7;

    public static string For(Trigger trigger, PullRequestState observed, string remote)
    {
        var text = new StringBuilder();
        var number = observed.PullRequest.Number;
        var head = observed.HeadCommit[..Math.Min(Head, observed.HeadCommit.Length)];

        switch (trigger.Reason)
        {
            case WakeReason.Conflict:
                text.Append(CultureInfo.InvariantCulture, $"Pull request #{number} conflicts with {observed.PullRequest.Base}. ");
                text.Append(CultureInfo.InvariantCulture, $"Avala fetched {remote}/{observed.PullRequest.Base}: merge it into your branch and resolve the conflicts.");
                break;
            case WakeReason.ChecksFailed:
                text.Append(CultureInfo.InvariantCulture, $"The checks of pull request #{number} failed on {head}:");

                foreach (var check in observed.Checks.Where(check => check.Status == CheckStatus.Failed))
                {
                    text.Append(CultureInfo.InvariantCulture, $"\n- {check.Name}");
                    text.Append(check.Summary.Length > 0 ? $": {check.Summary}" : string.Empty);
                    text.Append(check.Link.Match(link => $" ({link})", () => string.Empty));
                }

                text.Append("\nFix what makes them fail.");
                break;
            default:
                text.Append(CultureInfo.InvariantCulture, $"A review of pull request #{number} requests changes:");

                foreach (var review in observed.Reviews.Where(review => trigger.Key == $"review:{review.Id}"))
                {
                    text.Append(CultureInfo.InvariantCulture, $"\n{review.Author}: {review.Body}");

                    foreach (var comment in review.Comments)
                    {
                        text.Append(CultureInfo.InvariantCulture, $"\n- {comment.Path}{comment.Line.Match(line => $":{line}", () => string.Empty)}: {comment.Body}");
                    }
                }

                text.Append("\nMake the changes the review asks for.");
                break;
        }

        return $"{text}\n\n{Closing}";
    }

    public static string HeldComment(int wakeUps, WakeReason reason) =>
        string.Create(CultureInfo.InvariantCulture, $"Avala stopped after {wakeUps} wake-up{(wakeUps == 1 ? string.Empty : "s")} of the agent: {Describe(reason)}. A person needs to look.");

    public static string Describe(WakeReason reason) => reason switch
    {
        WakeReason.Conflict => "the pull request conflicts with its base",
        WakeReason.ChecksFailed => "its checks still fail",
        _ => "a review still requests changes",
    };
}
