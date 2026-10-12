namespace Avala.Components.Privacy;

public enum PersonalKind
{
    None,
    Email,
    Login,
    AccountName,
    Path,
    OrganizationId,
}

public interface IPersonalText
{
    string Show(bool hidden);
}

public sealed record Personal(PersonalKind Kind, string Value) : IPersonalText
{
    public static Personal None { get; } = new(PersonalKind.None, string.Empty);

    public string Masked => PersonalMask.Mask(Kind, Value);

    public static Personal Plain(string value) => new(PersonalKind.None, value);

    public static Personal Email(string value) => new(PersonalKind.Email, value);

    public static Personal Login(string value) => new(PersonalKind.Login, value);

    public static Personal AccountName(string value) => new(PersonalKind.AccountName, value);

    public static Personal Path(string value) => new(PersonalKind.Path, value);

    public static Personal OrganizationId(string value) => new(PersonalKind.OrganizationId, value);

    public static Personal Account(string label) => new(PersonalMask.KindOfAccount(label), label);

    public string Show(bool hidden) => hidden ? Masked : Value;

    public override string ToString() => Masked;
}

public sealed class PersonalPhrase(params object[] parts) : IPersonalText
{
    public static PersonalPhrase Empty { get; } = new();

    public bool IsEmpty => Show(hidden: false).Length == 0;

    public string Show(bool hidden) =>
        string.Concat(parts.Select(part => part is IPersonalText personal ? personal.Show(hidden) : part.ToString()));

    public override string ToString() => Show(hidden: true);
}
