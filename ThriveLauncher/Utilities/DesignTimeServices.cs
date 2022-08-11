using System;
using Microsoft.Extensions.DependencyInjection;

namespace ThriveLauncher.Utilities;

/// <summary>
///   Helpers for getting design time services for use in design time ViewModel constructors
/// </summary>
public static class DesignTimeServices
{
    private static IServiceProvider? rawServices;

    private static readonly Lazy<IServiceScope> Scope = new(() =>
    {
        rawServices = Program.BuildLauncherServices(false);
        return rawServices.CreateScope();
    });

    public static IServiceProvider Services => Scope.Value.ServiceProvider;
}
