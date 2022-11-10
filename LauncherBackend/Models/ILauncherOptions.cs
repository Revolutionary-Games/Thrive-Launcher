namespace LauncherBackend.Models;

using System.Collections.Generic;

/// <summary>
///   The options given on the command line when starting the launcher, these override somethings from
///   <see cref="LauncherSettings"/>
/// </summary>
public interface ILauncherOptions
{
    public bool SkipAutoUpdate { get; }

    public string? GameLDPreload { get; }

    public IList<string> ThriveExtraFlags { get; }
}
