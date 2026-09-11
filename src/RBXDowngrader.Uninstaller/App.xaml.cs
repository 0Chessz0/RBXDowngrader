using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;

namespace RBXDowngrader.Uninstaller;

public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var installDirectory = AppContext.BaseDirectory;
        var scopeMarker = Path.Combine(installDirectory, ".install-scope");
        var isAllUsers = File.Exists(scopeMarker) &&
            File.ReadAllText(scopeMarker).Trim().Equals("all-users", StringComparison.OrdinalIgnoreCase);

        if (isAllUsers && !IsAdministrator())
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath
                        ?? throw new InvalidOperationException("Could not locate the uninstaller."),
                    Arguments = "--elevated",
                    UseShellExecute = true,
                    Verb = "runas"
                });
                Shutdown();
                return;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                var canceledWindow = new MainWindow();
                canceledWindow.ShowStartupError("Administrator access is required");
                canceledWindow.Show();
                return;
            }
        }

        new MainWindow().Show();
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
