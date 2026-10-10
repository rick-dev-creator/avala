using System.Text.Json;
using Avala.Delegation.Contracts;
using Avala.Delegation.Policy;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Delegating;

internal sealed record DelegationInput(string Instruction, Option<Autonomy> Autonomy)
{
    public const int LongestInstruction = 4_000;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 2, AllowDuplicateProperties = false };

    public Option<ChildRole> Role { get; init; }

    public static Result<DelegationInput, DelegationError> Parse(string input)
    {
        try
        {
            using var document = JsonDocument.Parse(input, Options);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || root.EnumerateObject().Any(field => field.Name is not ("instruction" or "autonomy" or "role"))
                || !root.TryGetProperty("instruction", out var given)
                || given.ValueKind != JsonValueKind.String
                || given.GetString() is not { Length: > 0 and <= LongestInstruction } instruction
                || string.IsNullOrWhiteSpace(instruction))
            {
                return DelegationError.MalformedInput;
            }

            return AutonomyIn(root).Bind(autonomy => RoleIn(root).Map(role => new DelegationInput(instruction.Trim(), autonomy) { Role = role }));
        }
        catch (JsonException)
        {
            return DelegationError.MalformedInput;
        }
    }

    private static Result<Option<Autonomy>, DelegationError> AutonomyIn(JsonElement root) =>
        !root.TryGetProperty("autonomy", out var asked) ? Option<Autonomy>.None
        : asked.ValueKind != JsonValueKind.String ? DelegationError.MalformedInput
        : asked.GetString() switch
        {
            "supervised" => Option<Autonomy>.Some(Jobs.Contracts.Autonomy.Supervised),
            "autonomous" => Option<Autonomy>.Some(Jobs.Contracts.Autonomy.Autonomous),
            _ => DelegationError.MalformedInput,
        };

    private static Result<Option<ChildRole>, DelegationError> RoleIn(JsonElement root) =>
        !root.TryGetProperty("role", out var asked) ? Option<ChildRole>.None
        : asked.ValueKind != JsonValueKind.String ? DelegationError.MalformedInput
        : Roles.Named(asked.GetString() ?? string.Empty).Match(
            role => Result<Option<ChildRole>, DelegationError>.Success(role),
            () => DelegationError.MalformedInput);
}
