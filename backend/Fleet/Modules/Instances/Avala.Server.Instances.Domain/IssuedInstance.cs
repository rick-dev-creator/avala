namespace Avala.Server.Instances.Domain;

internal sealed record IssuedInstance(Instance Instance, string Key)
{
    public override string ToString() => $"{nameof(IssuedInstance)} {{ Instance = {Instance.Id.Value}, Key = {InstanceKey.Masked} }}";
}
