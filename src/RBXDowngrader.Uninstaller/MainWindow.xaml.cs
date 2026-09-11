using System.Diagnostics;
using System.IO;
using System.Security;
using System.Windows;
using Microsoft.Win32;

namespace RBXDowngrader.Uninstaller;

public partial class MainWindow : Window
{
    private static readonly string InstallDirectory = AppContext.BaseDirectory
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RBXDowngrader");

    public MainWindow() => InitializeComponent();

    public void ShowStartupError(string message)
    {
        Status.Text = message;
        Status.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(255, 111, 111));
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            UninstallButton.IsEnabled = false;
            Status.Text = "Removing";

            var marker = Path.Combine(InstallDirectory, ".install-scope");
            if (!File.Exists(marker))
                throw new InvalidOperationException("The installation marker is missing. No files were removed.");

            foreach (var process in Process.GetProcessesByName("RBXDowngrader"))
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(2000))
                    process.Kill(entireProcessTree: true);
            }

            RemoveRegistration();
            var script = CreateCleanupScript(KeepVersions.IsChecked == true);

            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                Arguments = $"/d /c \"\"{script}\"\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Status.Text = ex.Message;
            Status.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(255, 111, 111));
            UninstallButton.IsEnabled = true;
        }
    }

    private static void RemoveRegistration()
    {
        foreach (var programs in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
                 })
        {
            try
            {
                var shortcut = Path.Combine(programs, "RBXDowngrader.lnk");
                if (File.Exists(shortcut))
                    File.Delete(shortcut);
            }
            catch (UnauthorizedAccessException) { }
        }

        foreach (var registry in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                registry.DeleteSubKeyTree(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\RBXDowngrader",
                    throwOnMissingSubKey: false);
            }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
        }
    }

    private static string CreateCleanupScript(bool keepVersions)
    {
        var script = Path.Combine(Path.GetTempPath(), $"RBXDowngrader-cleanup-{Guid.NewGuid():N}.cmd");
        var installRoot = EscapeBatchPath(InstallDirectory);
        var dataRoot = EscapeBatchPath(DataDirectory);
        var sameDirectory = InstallDirectory.Equals(DataDirectory, StringComparison.OrdinalIgnoreCase);
        var lines = new List<string>
        {
            "@echo off",
            "ping 127.0.0.1 -n 3 > nul"
        };

        if (sameDirectory)
        {
            AddDataCleanup(lines, installRoot, keepVersions);
        }
        else
        {
            lines.Add($"rd /s /q \"{installRoot}\"");
            AddDataCleanup(lines, dataRoot, keepVersions);
        }

        lines.Add("del /f /q \"%~f0\"");
        File.WriteAllLines(script, lines);
        return script;
    }

    private static void AddDataCleanup(List<string> lines, string root, bool keepVersions)
    {
        if (keepVersions)
        {
            lines.Add($"if exist \"{root}\" for /d %%D in (\"{root}\\*\") do if /I not \"%%~nxD\"==\"robloxversions\" rd /s /q \"%%~fD\"");
            lines.Add($"if exist \"{root}\" for %%F in (\"{root}\\*\") do del /f /q \"%%~fF\"");
        }
        else
        {
            lines.Add($"rd /s /q \"{root}\"");
        }
    }

    private static string EscapeBatchPath(string path) =>
        path.Replace("%", "%%", StringComparison.Ordinal);

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
