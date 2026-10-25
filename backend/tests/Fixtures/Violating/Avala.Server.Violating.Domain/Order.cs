using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Avala.Server.Violating.Domain;

[Index(nameof(Code))]
public sealed class Order
{
    [Key]
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;
}
