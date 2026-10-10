using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Agents.Contracts.Capabilities;

public sealed record OffersModels(ValueList<string> Models, ValueList<string> Efforts) : ICapability
{
    public const string ModelSetting = "model";

    public const string EffortSetting = "effort";

    public OffersModels(string[] models, string[] efforts)
        : this(new ValueList<string>(models), new ValueList<string>(efforts))
    {
    }

    public Option<string> DefaultModel { get; init; }

    public Option<string> DefaultEffort { get; init; }

    public ModelChoice DefaultChoice() => new(DefaultModel, DefaultEffort);

    public static ModelChoice SettingsOf(IReadOnlyDictionary<string, string> settings) =>
        new(Setting(settings, ModelSetting), Setting(settings, EffortSetting));

    public OffersModels WithDefaults(ModelChoice defaults) =>
        this with { DefaultModel = defaults.Model.IsSome ? defaults.Model : DefaultModel, DefaultEffort = defaults.Effort.IsSome ? defaults.Effort : DefaultEffort };

    public Option<ModelRefusal> Refuses(ModelChoice choice) =>
        choice.Model.Match(model => !Models.Contains(model), () => false) ? ModelRefusal.UnofferedModel
        : choice.Effort.Match(effort => !Efforts.Contains(effort), () => false) ? ModelRefusal.UnofferedEffort
        : Option<ModelRefusal>.None;

    public ModelChoice Applied(ModelChoice choice) => choice.Or(DefaultChoice());

    public static Option<ModelRefusal> RefusalOn(CapabilitySet capabilities, ModelChoice choice) =>
        capabilities.Get<OffersModels>().Match(
            offered => offered.Refuses(choice),
            () => choice.Model.IsSome ? ModelRefusal.UnofferedModel
                : choice.Effort.IsSome ? ModelRefusal.UnofferedEffort
                : Option<ModelRefusal>.None);

    public static Option<ModelRefusal> DefaultsRefusedOn(CapabilitySet capabilities) =>
        capabilities.Get<OffersModels>().Bind(offered => offered.Refuses(offered.DefaultChoice()));

    private static Option<string> Setting(IReadOnlyDictionary<string, string> settings, string name) =>
        settings.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : Option<string>.None;
}

public enum ModelRefusal
{
    UnofferedModel,
    UnofferedEffort,
}
