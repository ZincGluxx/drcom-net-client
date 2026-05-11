using System;
using System.Threading;
using System.Windows.Forms;
using CampusNetworkLogin.Forms;

namespace CampusNetworkLogin;

internal static class Program
{
    /// <summary>
    /// 应用程序入口点
    /// </summary>
    [STAThread]
    static void Main()
    {
        // 确保只有一个实例运行
        using var mutex = new Mutex(true, "DrcomNET-SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Drcom .NET 已在运行中！", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"程序异常: {ex.Message}\n{ex.StackTrace}",
                "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
