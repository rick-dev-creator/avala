namespace Avala.Server.Activity.Domain;

internal static class JobIds
{
    public static Admission<Guid> Admit(Guid id) =>
        id == Guid.Empty ? Admission.Refuse<Guid>("the job id is empty") : Admission.Admit(id);
}
