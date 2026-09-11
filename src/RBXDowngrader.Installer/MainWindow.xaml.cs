using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Principal;
using System.Windows;
using Microsoft.Win32;

namespace RBXDowngrader.Installer;

public partial class MainWindow : Window
{
    private static readonly string UserInstallDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RBXDowngrader");

    private static readonly string AllUsersInstallDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RBXDowngrader");

    private string _installDirectory = UserInstallDirectory;
    private bool _customizationVisible;
    private bool _installForAllUsers;

    public MainWindow()
    {
        InitializeComponent();
        DirectoryInput.Text = UserInstallDirectory;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var autoInstall = arguments.Contains("--install", StringComparer.OrdinalIgnoreCase);
        _installForAllUsers = arguments.Contains("--all-users", StringComparer.OrdinalIgnoreCase);
        AllUsersCheckBox.IsChecked = _installForAllUsers;

        var directoryIndex = Array.FindIndex(arguments,
            argument => argument.Equals("--directory", StringComparison.OrdinalIgnoreCase));
        if (directoryIndex >= 0 && directoryIndex + 1 < arguments.Length)
            DirectoryInput.Text = arguments[directoryIndex + 1];

        if (autoInstall)
            Dispatcher.BeginInvoke(() => Install_Click(InstallButton, new RoutedEventArgs()));
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _installDirectory = NormalizeInstallDirectory(DirectoryInput.Text);
            _installForAllUsers = AllUsersCheckBox.IsChecked == true;

            if (_installForAllUsers && !IsAdministrator())
            {
                RelaunchElevated(_installDirectory);
                Close();
                return;
            }

            SetControlsEnabled(false);
            Progress.Visibility = Visibility.Visible;
            Status.Text = "Installing";

            await Task.Run(Install);
            Progress.Value = 100;
            Status.Text = "Installed";
            CustomizeButton.Visibility = Visibility.Collapsed;
            InstallButton.Content = "Open RBXDowngrader";
            InstallButton.IsEnabled = true;
            InstallButton.Click -= Install_Click;
            InstallButton.Click += Open_Click;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Status.Text = "Administrator access was canceled";
            SetControlsEnabled(true);
        }
        catch (Exception ex)
        {
            Status.Text = ex.Message;
            Status.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(255, 111, 111));
            SetControlsEnabled(true);
        }
    }

    private void Install()
    {
        using var payload = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("RBXDowngrader.Payload.zip")
            ?? throw new InvalidOperationException("Installer payload is missing. Run the packaging script first.");

        Directory.CreateDirectory(_installDirectory);
        var root = Path.GetFullPath(_installDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read);
        var processed = 0;

        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The installer payload contains an unsafe path.");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            processed++;
            Dispatcher.Invoke(() => Progress.Value = processed * 85d / archive.Entries.Count);
        }

        var appPath = Path.Combine(_installDirectory, "RBXDowngrader.exe");
        var uninstallPath = Path.Combine(_installDirectory, "uninstall.exe");
        if (!File.Exists(appPath) || !File.Exists(uninstallPath))
            throw new InvalidDataException("The installer payload is incomplete.");

        File.WriteAllText(Path.Combine(_installDirectory, ".install-scope"),
            _installForAllUsers ? "all-users" : "current-user");
        CreateShortcut(appPath, _installForAllUsers);
        RegisterUninstaller(appPath, uninstallPath, _installForAllUsers);
    }

    private void CreateShortcut(string appPath, bool allUsers)
    {
        var programs = Environment.GetFolderPath(allUsers
            ? Environment.SpecialFolder.CommonPrograms
            : Environment.SpecialFolder.Programs);
        var shortcutPath = Path.Combine(programs, "RBXDowngrader.lnk");
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows shortcut support is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = appPath;
        shortcut.WorkingDirectory = _installDirectory;
        shortcut.IconLocation = $"{appPath},0";
        shortcut.Description = "Launch legacy Roblox versions";
        shortcut.Save();
    }

    private void RegisterUninstaller(string appPath, string uninstallPath, bool allUsers)
    {
        var registry = allUsers ? Registry.LocalMachine : Registry.CurrentUser;
        using var key = registry.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\RBXDowngrader", writable: true);
        key.SetValue("DisplayName", "RBXDowngrader");
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "RBXDowngrader");
        key.SetValue("InstallLocation", _installDirectory);
        key.SetValue("DisplayIcon", appPath);
        key.SetValue("UninstallString", $"\"{uninstallPath}\"");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static string NormalizeInstallDirectory(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Choose an installation directory.");

        var result = Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim()))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (result.Equals(Path.GetPathRoot(result), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a folder instead of a drive root.");

        var versionsDirectory = Path.Combine(UserInstallDirectory, "robloxversions") + Path.DirectorySeparatorChar;
        if ((result + Path.DirectorySeparatorChar).StartsWith(versionsDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The app cannot be installed inside the versions folder.");

        if (Directory.Exists(result) &&
            !File.Exists(Path.Combine(result, ".install-scope")) &&
            Directory.EnumerateFileSystemEntries(result).Any())
        {
            throw new InvalidOperationException("Choose an empty folder or an existing RBXDowngrader installation.");
        }
        return result;
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void RelaunchElevated(string installDirectory)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not locate the installer executable.");
        Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = $"--all-users --directory \"{installDirectory}\" --install",
            UseShellExecute = true,
            Verb = "runas"
        });
    }

    private void Customize_Click(object sender, RoutedEventArgs e)
    {
        _customizationVisible = !_customizationVisible;
        CustomizationPanel.Visibility = _customizationVisible ? Visibility.Visible : Visibility.Collapsed;
        CustomizeButton.Content = _customizationVisible ? "Hide customization" : "Customize installation";
        Height = _customizationVisible ? 570 : 430;
    }

    private void Scope_Changed(object sender, RoutedEventArgs e)
    {
        if (DirectoryInput is null)
            return;

        if (string.IsNullOrWhiteSpace(DirectoryInput.Text) ||
            DirectoryInput.Text.Equals(UserInstallDirectory, StringComparison.OrdinalIgnoreCase) ||
            DirectoryInput.Text.Equals(AllUsersInstallDirectory, StringComparison.OrdinalIgnoreCase))
        {
            DirectoryInput.Text = AllUsersCheckBox.IsChecked == true
                ? AllUsersInstallDirectory
                : UserInstallDirectory;
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose installation directory",
            InitialDirectory = Directory.Exists(DirectoryInput.Text)
                ? DirectoryInput.Text
                : Path.GetDirectoryName(DirectoryInput.Text) ?? string.Empty,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
            DirectoryInput.Text = Path.Combine(dialog.FolderName, "RBXDowngrader");
    }

    private void SetControlsEnabled(bool enabled)
    {
        InstallButton.IsEnabled = enabled;
        CustomizeButton.IsEnabled = enabled;
        DirectoryInput.IsEnabled = enabled;
        AllUsersCheckBox.IsEnabled = enabled;
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(Path.Combine(_installDirectory, "RBXDowngrader.exe"))
        {
            UseShellExecute = true,
            WorkingDirectory = _installDirectory
        });
        Close();
    }

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
