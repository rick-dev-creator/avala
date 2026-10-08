using Microsoft.Extensions.DependencyInjection;

namespace Avala.Shell;

public static class ShellServices
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddShell() => services.AddSingleton<ShellViewModel>();
    }
}
