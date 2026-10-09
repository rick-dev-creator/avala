using Avala.Testing;
using Avala.Workbench.Review;
using Avala.Workbench.Reviewing;

namespace Avala.Workbench.Tests.Review;

public sealed class ReviewExceptionViewModelScripts
{
    [Fact]
    public void AnEditedRuleFileSaysWhichAndWhyItDoesNotApply() =>
        ViewModelScript.Given(new ReviewExceptionViewModel(new EditedRuleFile(".avala/permissions.json")))
            .Then(exception => Assert.Equal(
                ("The agent edited a rule file", ".avala/permissions.json", "Its rules apply from the base commit, not from this edit.", ExceptionTone.Attention),
                (exception.Title, exception.Fact, exception.Detail, exception.Tone)));

    [Fact]
    public void AnUnreadableDiffSaysNothingProvesTheRulesAreUntouched() =>
        ViewModelScript.Given(new ReviewExceptionViewModel(new UnreadableChanges()))
            .Then(exception => Assert.Equal(("The diff could not be read", "Nothing proves the rule files are untouched.", ExceptionTone.Attention), (exception.Title, exception.Detail, exception.Tone)));

    [Fact]
    public void AnExceptionStartsFoldedAndRemembersBeingOpened() =>
        ViewModelScript.Given(new ReviewExceptionViewModel(new UnreadableChanges()))
            .When(exception => exception.IsExpanded = true)
            .ThenNotified(nameof(ReviewExceptionViewModel.IsExpanded))
            .Then(exception => Assert.True(exception.IsExpanded));
}
