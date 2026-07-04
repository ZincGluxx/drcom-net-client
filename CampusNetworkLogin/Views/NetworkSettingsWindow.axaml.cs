using Avalonia.Controls;
using Avalonia.Interactivity;
using CampusNetworkLogin.Services;
using System;

namespace CampusNetworkLogin.Views;

public partial class NetworkSettingsWindow : Window
{
    private readonly string _adapterName;

    public NetworkSettingsWindow()
    {
        InitializeComponent();
        _adapterName = "";
    }

    public NetworkSettingsWindow(NetworkSnapshot snapshot)
    {
        InitializeComponent();
        _adapterName = snapshot.AdapterName;
        AdapterText.Text = $"网卡: {snapshot.AdapterName}";
        IpField.Text = snapshot.Ip == "0.0.0.0" ? "" : snapshot.Ip;
        SubnetField.Text = string.IsNullOrWhiteSpace(snapshot.SubnetMask) ? "255.255.255.0" : snapshot.SubnetMask;
        GatewayField.Text = snapshot.Gateway == "--" ? "" : snapshot.Gateway;
        DnsField.Text = snapshot.Dns == "--" ? "10.10.10.10" : snapshot.Dns;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void Apply_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            ErrorText.Text = "";
            await NetworkSettingsService.ApplyStaticAsync(
                _adapterName,
                IpField.Text?.Trim() ?? "",
                SubnetField.Text?.Trim() ?? "",
                GatewayField.Text?.Trim() ?? "",
                DnsField.Text?.Trim() ?? "");

            Close(true);
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }
}
