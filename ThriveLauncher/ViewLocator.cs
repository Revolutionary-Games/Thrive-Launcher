using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Microsoft.Extensions.DependencyInjection;
using ThriveLauncher.ViewModels;

namespace ThriveLauncher;

public class ViewLocator : IDataTemplate
{
    private readonly IServiceProvider serviceProvider;

    public ViewLocator(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    public IControl Build(object data)
    {
        var name = data.GetType().FullName!.Replace("ViewModel", "View");
        var type = Type.GetType(name);

        if (type != null)
        {
            var scope = serviceProvider.CreateScope();

            return (Control)scope.ServiceProvider.GetRequiredService(type);
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object data)
    {
        return data is ViewModelBase;
    }
}
