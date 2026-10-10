using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.ModelChoices;

namespace Avala.Workbench.Tests.ModelChoices;

public sealed class ModelPickerViewModelScripts
{
    private static readonly OffersModels Offered = new(["large", "small"], ["low", "high"]) { DefaultModel = "small" };

    [Fact]
    public void AnOfferListsItsDefaultFirstThenEveryModelAndEffortAndChoosesNothingUntilPicked() =>
        ViewModelScript.Given(new ModelPickerViewModel())
            .When(picker => picker.Offer(Offered, new ModelDefaults("Default (small)", "Default"), ModelChoice.Default, "note"))
            .ThenNotified(nameof(ModelPickerViewModel.Models), nameof(ModelPickerViewModel.Efforts), nameof(ModelPickerViewModel.IsShown), nameof(ModelPickerViewModel.Note))
            .Then(picker => Assert.Equal(
                (true, true, true, "Default (small)", "Default", "note"),
                (picker.IsShown, picker.CanChoose, picker.IsEffortShown, picker.Model, picker.Effort, picker.Note)))
            .Then(picker => Assert.Equal<string>(["Default (small)", "large", "small"], picker.Models))
            .Then(picker => Assert.Equal<string>(["Default", "low", "high"], picker.Efforts))
            .Then(picker => Assert.True(picker.Chosen.IsDefault));

    [Fact]
    public void APickedModelAndEffortAreTheChoice() =>
        ViewModelScript.Given(Offering())
            .When(picker =>
            {
                picker.Model = "large";
                picker.Effort = "high";
            })
            .Then(picker => Assert.Equal(new ModelChoice("large", "high"), picker.Chosen));

    [Fact]
    public void AnOfferStartsFromTheSelectionItIsGivenAndAnotherOfferKeepsWhatItStillOffers() =>
        ViewModelScript.Given(new ModelPickerViewModel())
            .When(picker => picker.Offer(Offered, ModelPhrases.Connection, new ModelChoice("large", Option<string>.None), "note"))
            .Then(picker => Assert.Equal(new ModelChoice("large", Option<string>.None), picker.Chosen))
            .When(picker => picker.Offer(Offered with { Models = new ValueList<string>(["small"]) }, ModelPhrases.Connection, ModelChoice.Default, "note"))
            .Then(picker => Assert.Equal(ModelPhrases.HarnessDefault, picker.Model));

    [Fact]
    public void AHarnessWithoutEffortsShowsNoEffortAndChoosesNone() =>
        ViewModelScript.Given(new ModelPickerViewModel())
            .When(picker => picker.Offer(new OffersModels(["fast"], []), ModelPhrases.Connection, new ModelChoice(Option<string>.None, "high"), "note"))
            .Then(picker => Assert.Equal((false, ModelChoice.Default), (picker.IsEffortShown, picker.Chosen)));

    [Fact]
    public void FollowingShowsOnlyTheDefaultOfTheConnectionAvalaChoosesAndChoosesNothing() =>
        ViewModelScript.Given(Offering())
            .When(picker =>
            {
                picker.Model = "large";
                picker.Follow(new ModelDefaults("Default (small)", "Default"), effort: true, "Auto picks…");
            })
            .Then(picker => Assert.Equal(
                (true, false, "Default (small)", "Auto picks…", true),
                (picker.IsShown, picker.CanChoose, picker.Model, picker.Note, picker.Chosen.IsDefault)))
            .Then(picker => Assert.Equal<string>(["Default (small)"], picker.Models));

    [Fact]
    public void HidingShowsNoPickerAndChoosesNothing() =>
        ViewModelScript.Given(Offering())
            .When(picker => picker.Hide("work offers no choice of model: its harness runs its own."))
            .Then(picker => Assert.Equal(
                (false, false, "work offers no choice of model: its harness runs its own.", true),
                (picker.IsShown, picker.IsEffortShown, picker.Note, picker.Chosen.IsDefault)));

    private static ModelPickerViewModel Offering()
    {
        var picker = new ModelPickerViewModel();
        picker.Offer(Offered, new ModelDefaults("Default (small)", "Default"), ModelChoice.Default, "note");

        return picker;
    }
}
