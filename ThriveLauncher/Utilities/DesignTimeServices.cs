namespace ThriveLauncher.Utilities;

using System;
using Microsoft.Extensions.DependencyInjection;
using Models;

/// <summary>
///   Helpers for getting design time services for use in design time ViewModel constructors
/// </summary>
public static class DesignTimeServices
{
    private static readonly Lazy<IServiceScope> Scope = new(() =>
    {
        rawServices = Program.BuildLauncherServices(false, new Options());
        return rawServices.CreateScope();
    });

    private static IServiceProvider? rawServices;

    public static IServiceProvider Services => Scope.Value.ServiceProvider;
}
