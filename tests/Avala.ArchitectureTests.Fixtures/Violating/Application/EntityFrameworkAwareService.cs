using Microsoft.EntityFrameworkCore;

namespace Avala.Fixtures.Violating.Application;

public sealed class EntityFrameworkAwareService
{
    public EntityState Tracking => EntityState.Unchanged;
}
