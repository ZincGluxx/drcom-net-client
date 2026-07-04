namespace CampusNetworkLogin.Models;

/// <summary>
/// 校园网登录配置模型
/// </summary>
public class ConfigModel
{
    public string Server { get; set; } = "10.100.61.3";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string HostIp { get; set; } = "0.0.0.0";
    public string Mac { get; set; } = "0x888888888888";
    public string Gateway { get; set; } = "0.0.0.0";
    public string HostName { get; set; } = "";
    public string HostOs { get; set; } = "Windows 10";
    public string PrimaryDns { get; set; } = "10.10.10.10";
    public string DhcpServer { get; set; } = "0.0.0.0";
    public bool AutoLogin { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
}
