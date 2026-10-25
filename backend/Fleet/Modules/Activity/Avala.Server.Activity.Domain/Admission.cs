namespace Avala.Server.Activity.Domain;

internal sealed class Admission<T>
{
    private readonly T? value;

    internal Admission(T? value, string? refusal)
    {
        this.value = value;
        Refusal = refusal;
    }

    public string? Refusal { get; }

    public bool IsAdmitted => Refusal is null;

    public T Value => IsAdmitted ? value! : throw new InvalidOperationException($"The value was refused: {Refusal}");
}

internal static class Admission
{
    public static Admission<T> Admit<T>(T value) => new(value, null);

    public static Admission<T> Refuse<T>(string reason) => new(default, reason);
}
