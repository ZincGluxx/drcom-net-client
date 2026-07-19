using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CampusNetworkLogin.Models;
using CampusNetworkLogin.Services;
using CampusNetworkLogin.Views;
using System.Threading.Tasks;

namespace CampusNetworkLogin;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var configTask = new ConfigService().LoadAsync();
            desktop.MainWindow = new MainWindow(configTask);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
