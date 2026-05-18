using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CampusNetworkLogin.Views;

public partial class ConfigPageView : UserControl
{
    public event EventHandler<RoutedEventArgs>? SaveClicked;

    public ConfigPageView()
    {
        InitializeComponent();
    }

    private void SaveBtn_Click(object? sender, RoutedEventArgs e) => SaveClicked?.Invoke(sender, e);
}
