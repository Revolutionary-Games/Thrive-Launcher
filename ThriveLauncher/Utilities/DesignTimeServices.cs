using System;
using Microsoft.Extensions.DependencyInjection;

namespace ThriveLauncher.Utilities;

/// <summary>
///   Helpers for getting design time services for use
/// </summary>
public static class DesignTimeServices
{
    private static readonly Lazy<IServiceScope> Scope = new(() => Program.BuildLauncherServices(false).CreateScope());

    public static IServiceProvider Services => Scope.Value.ServiceProvider;
}
