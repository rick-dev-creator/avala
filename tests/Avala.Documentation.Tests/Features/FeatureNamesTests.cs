using Avala.Documentation.Contracts;
using Avala.Documentation.Features;

namespace Avala.Documentation.Tests.Features;

public sealed class FeatureNamesTests
{
    private static readonly Feature Payments = new("payments-on-stripe-go-v79", "Payments on stripe-go v79");
    private static readonly Feature Payouts = new("payout-scheduler", "Payout scheduler");
    private static readonly Feature Refunds = new("refund-webhooks", "Refund webhooks");

    [Theory]
    [InlineData("Refund webhooks", "refund-webhooks")]
    [InlineData("Payments on stripe-go v79", "payments-on-stripe-go-v79")]
    [InlineData("  Facturación   en línea!  ", "facturacion-en-linea")]
    [InlineData("C# & .NET 10 upgrade", "c-net-10-upgrade")]
    [InlineData("???", "feature")]
    public void AFeaturesFolderIsTheSlugOfItsTitle(string title, string slug) =>
        Assert.Equal(slug, FeatureNames.Slug(title));

    [Fact]
    public void ASlugIsCutAtAWordToStayShort() =>
        Assert.Equal("move-every-stripe-call-to-the-v79-client-behind", FeatureNames.Slug("Move every Stripe call to the v79 client behind the Payments container today"));

    [Theory]
    [InlineData("Handle Stripe refund webhooks and update the ledger", "refund-webhooks")]
    [InlineData("Retry the payout scheduler when the bank is closed", "payout-scheduler")]
    [InlineData("Bump stripe-go payments to v79 everywhere", "payments-on-stripe-go-v79")]
    public void TheSuggestionIsTheExistingFeatureWhoseTitleTheInstructionNamesMost(string instruction, string slug) =>
        Assert.Equal(slug, FeatureNames.Suggest(instruction, [Payments, Payouts, Refunds]).Feature.Slug);

    [Fact]
    public void AnInstructionThatNamesNoFeatureSuggestsANewOneTitledFromItsFirstWords()
    {
        var suggestion = FeatureNames.Suggest("Add dark mode to the settings page, then update the screenshots.", [Payments, Payouts]);

        Assert.Equal((new Feature("add-dark-mode-to-the-settings-page", "Add dark mode to the settings page"), true), (suggestion.Feature, suggestion.IsNew));
    }

    [Theory]
    [InlineData("[WIP] Handle Stripe refund webhooks", "Handle Stripe refund webhooks")]
    [InlineData("[simulate: plan-revised] [team] Handle Stripe refund webhooks", "Handle Stripe refund webhooks")]
    [InlineData("Fix: the refund webhook drops events", "The refund webhook drops events")]
    [InlineData("Refactor.", "Refactor")]
    [InlineData("Create a tiny script greet.py that prints a greeting. Then document it.", "Create a tiny script greet.py that prints")]
    [InlineData("Create a tiny command-line Python script greet.py that prints a greeting", "Create a tiny command-line Python script greet.py")]
    [InlineData("Bump the version to 2.1.0, then tag it", "Bump the version to 2.1.0")]
    public void ANewFeaturesTitleSkipsLeadingTagsAndALoneWordBeforeAColonAndEndsOnAWordThatCarriesMeaning(string instruction, string title) =>
        Assert.Equal(title, FeatureNames.New(instruction).Title);

    [Fact]
    public void AnExistingFeatureIsSuggestedOnlyWhenTheInstructionNamesMoreThanHalfItsTitle() =>
        Assert.True(FeatureNames.Suggest("Fix the scheduler of the nightly backup", [Payouts]).IsNew);
}
