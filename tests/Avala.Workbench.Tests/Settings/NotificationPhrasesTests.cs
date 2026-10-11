using Avala.Notifications.Contracts;
using Avala.Workbench.Settings;

namespace Avala.Workbench.Tests.Settings;

public sealed class NotificationPhrasesTests
{
    [Fact]
    public void EveryNotificationErrorIsExplainedInItsOwnSentence()
    {
        var sentences = Enum.GetValues<NotificationError>().Select(NotificationPhrases.Sentence).ToList();

        Assert.Equal(sentences.Count, sentences.Distinct(StringComparer.Ordinal).Count());
        Assert.All(sentences, sentence => Assert.Matches("^[A-Za-z].*[^.]\\.$", sentence));
    }
}
