using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DrComCampus.Models;
using DrComCampus.Services;
using DrComCampus.Views;

namespace DrComCampus;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var configTask = new ConfigurationService().LoadAsync();
            desktop.MainWindow = new CampusLoginWindow(configTask);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
