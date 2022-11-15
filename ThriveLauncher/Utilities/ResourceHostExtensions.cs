namespace ThriveLauncher.Utilities;

using System;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

public static class ResourceHostExtensions
{
    // The following two methods (with modifications) are from:
    // https://www.reddit.com/r/AvaloniaUI/comments/ssplp9/comment/hx0e3zi/?utm_source=share&utm_medium=web2x&context=3
    public static IServiceProvider GetServiceProvider(this IResourceHost control)
    {
        return (IServiceProvider?)control.FindResource(typeof(IServiceProvider)) ??
            throw new Exception("Expected service provider missing");
    }

    public static T CreateInstance<T>(this IResourceHost control)
    {
        return ActivatorUtilities.CreateInstance<T>(control.GetServiceProvider());
    }
}
