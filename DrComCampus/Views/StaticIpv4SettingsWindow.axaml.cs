using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DrComCampus.Services;

namespace DrComCampus.Views;

public sealed partial class StaticIpv4SettingsWindow : Window
{
    private readonly string _adapterName;

    public StaticIpv4SettingsWindow()
    {
        InitializeComponent();
        _adapterName = "";
    }

    internal StaticIpv4SettingsWindow(NetworkSnapshot snapshot)
    {
        InitializeComponent();
        ArgumentNullException.ThrowIfNull(snapshot);
        _adapterName = snapshot.AdapterName;
        AdapterNameText.Text = $"网卡: {snapshot.AdapterName}";
        IpAddressTextBox.Text = snapshot.Ipv4Address == "0.0.0.0" ? "" : snapshot.Ipv4Address;
        SubnetMaskTextBox.Text = string.IsNullOrWhiteSpace(snapshot.SubnetMask) ? "255.255.255.0" : snapshot.SubnetMask;
        GatewayTextBox.Text = snapshot.Gateway == "--" ? "" : snapshot.Gateway;
        DnsServerTextBox.Text = snapshot.DnsServer == "--" ? "10.10.10.10" : snapshot.DnsServer;
    }

    private void OnCancelButtonClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void OnApplyButtonClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            ValidationErrorText.Text = "";
            await NetworkSettingsService.ApplyStaticIpv4ConfigurationAsync(
                _adapterName,
                IpAddressTextBox.Text?.Trim() ?? "",
                SubnetMaskTextBox.Text?.Trim() ?? "",
                GatewayTextBox.Text?.Trim() ?? "",
                DnsServerTextBox.Text?.Trim() ?? "");

            Close(true);
        }
        catch (Exception ex)
        {
            ValidationErrorText.Text = ex.Message;
        }
    }
}
