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
                ("Edited a rule file: .avala/permissions.json", "Its rules apply from the base commit, not from this edit."),
                (exception.Title, exception.Detail)));

    [Fact]
    public void AnUnreadableDiffSaysNothingProvesTheRulesAreUntouched() =>
        ViewModelScript.Given(new ReviewExceptionViewModel(new UnreadableChanges()))
            .Then(exception => Assert.Equal(("The diff could not be read", "Nothing proves the rule files are untouched."), (exception.Title, exception.Detail)));
}
