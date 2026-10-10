using System.Text.Json;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Delegating;

internal sealed record DelegationInput(string Instruction, Option<Autonomy> Autonomy)
{
    public ModelChoice Model { get; init; } = ModelChoice.Default;

    public const int LongestInstruction = 4_000;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 2, AllowDuplicateProperties = false };

    public static Result<DelegationInput, DelegationError> Parse(string input)
    {
        try
        {
            using var document = JsonDocument.Parse(input, Options);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || root.EnumerateObject().Any(field => field.Name is not ("instruction" or "autonomy" or "model" or "effort"))
                || !root.TryGetProperty("instruction", out var given)
                || given.ValueKind != JsonValueKind.String
                || given.GetString() is not { Length: > 0 and <= LongestInstruction } instruction
                || string.IsNullOrWhiteSpace(instruction))
            {
                return DelegationError.MalformedInput;
            }

            return AutonomyIn(root).Bind(autonomy => Named(root, "model").Bind(model => Named(root, "effort")
                .Map(effort => new DelegationInput(instruction.Trim(), autonomy) { Model = new ModelChoice(model, effort) })));
        }
        catch (JsonException)
        {
            return DelegationError.MalformedInput;
        }
    }

    private static Result<Option<string>, DelegationError> Named(JsonElement root, string field) =>
        !root.TryGetProperty(field, out var asked) ? Option<string>.None
        : asked.ValueKind == JsonValueKind.String && asked.GetString() is { } named && !string.IsNullOrWhiteSpace(named) ? Option<string>.Some(named.Trim())
        : DelegationError.MalformedInput;

    private static Result<Option<Autonomy>, DelegationError> AutonomyIn(JsonElement root) =>
        !root.TryGetProperty("autonomy", out var asked) ? Option<Autonomy>.None
        : asked.ValueKind != JsonValueKind.String ? DelegationError.MalformedInput
        : asked.GetString() switch
        {
            "supervised" => Option<Autonomy>.Some(Jobs.Contracts.Autonomy.Supervised),
            "autonomous" => Option<Autonomy>.Some(Jobs.Contracts.Autonomy.Autonomous),
            _ => DelegationError.MalformedInput,
        };
}
