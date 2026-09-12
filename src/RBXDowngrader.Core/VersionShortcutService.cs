using System.Runtime.InteropServices;

namespace RBXDowngrader.Core;

public sealed record VersionShortcutState(bool Desktop, bool StartMenu);

public sealed class VersionShortcutService
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private readonly string _desktopDirectory;
    private readonly string _startMenuDirectory;

    public VersionShortcutService(string? desktopDirectory = null, string? startMenuDirectory = null)
    {
        _desktopDirectory = desktopDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        _startMenuDirectory = startMenuDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);
    }

    public VersionShortcutState GetState(InstalledVersion version) => new(
        HasMatchingShortcut(_desktopDirectory, version),
        HasMatchingShortcut(_startMenuDirectory, version));

    public void SetState(InstalledVersion version, bool desktop, bool startMenu)
    {
        var previousDesktop = HasMatchingShortcut(_desktopDirectory, version);
        SetShortcut(_desktopDirectory, version, desktop);
        try
        {
            SetShortcut(_startMenuDirectory, version, startMenu);
        }
        catch
        {
            SetShortcut(_desktopDirectory, version, previousDesktop);
            throw;
        }
    }

    public void RenameExisting(InstalledVersion previous, InstalledVersion renamed)
    {
        if (previous.DisplayName.Equals(renamed.DisplayName, StringComparison.Ordinal))
            return;

        var moves = new List<ShortcutMove>();
        AddRenameMove(_desktopDirectory, "the Desktop", previous, renamed, moves);
        AddRenameMove(_startMenuDirectory, "the Start Menu", previous, renamed, moves);

        foreach (var move in moves)
        {
            if (File.Exists(move.Destination)
                && !PathsEqual(move.Source, move.Destination))
            {
                throw new InvalidOperationException(
                    $"A shortcut named '{renamed.DisplayName}' already exists in {move.LocationName}.");
            }
        }

        var completed = new Stack<ShortcutMove>();
        try
        {
            foreach (var move in moves)
            {
                MoveShortcut(move.Source, move.Destination);
                completed.Push(move);
            }
        }
        catch
        {
            while (completed.TryPop(out var move))
            {
                try { MoveShortcut(move.Destination, move.Source); }
                catch { }
            }
            throw;
        }
    }

    public void Remove(InstalledVersion version)
    {
        RemoveMatchingShortcut(_desktopDirectory, version);
        RemoveMatchingShortcut(_startMenuDirectory, version);
    }

    public static bool IsValidDisplayName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.EndsWith(' ')
            || name.EndsWith('.')
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        var stem = name.Split('.')[0];
        return !ReservedNames.Contains(stem);
    }

    private static void AddRenameMove(
        string directory,
        string locationName,
        InstalledVersion previous,
        InstalledVersion renamed,
        ICollection<ShortcutMove> moves)
    {
        var previousPath = TryGetShortcutPath(directory, previous.DisplayName);
        if (previousPath is null || !File.Exists(previousPath)
            || !ShortcutTargets(previousPath, previous.ExecutablePath))
        {
            return;
        }

        var destination = GetShortcutPath(directory, renamed.DisplayName);
        moves.Add(new ShortcutMove(
            previousPath,
            destination,
            locationName));
    }

    private static void SetShortcut(string directory, InstalledVersion version, bool shouldExist)
    {
        var path = GetShortcutPath(directory, version.DisplayName);
        if (!shouldExist)
        {
            if (File.Exists(path) && ShortcutTargets(path, version.ExecutablePath))
                File.Delete(path);
            return;
        }

        if (!File.Exists(version.ExecutablePath))
            throw new FileNotFoundException("RobloxPlayerBeta.exe is missing.", version.ExecutablePath);
        Directory.CreateDirectory(directory);
        if (File.Exists(path) && !ShortcutTargets(path, version.ExecutablePath))
            throw new InvalidOperationException($"A shortcut named '{version.DisplayName}' already exists.");

        CreateShortcut(path, version);
    }

    private static bool HasMatchingShortcut(string directory, InstalledVersion version)
    {
        var path = TryGetShortcutPath(directory, version.DisplayName);
        return path is not null && File.Exists(path) && ShortcutTargets(path, version.ExecutablePath);
    }

    private static void RemoveMatchingShortcut(string directory, InstalledVersion version)
    {
        var path = TryGetShortcutPath(directory, version.DisplayName);
        if (path is not null && File.Exists(path) && ShortcutTargets(path, version.ExecutablePath))
            File.Delete(path);
    }

    private static void CreateShortcut(string path, InstalledVersion version)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Version shortcuts are only supported on Windows.");

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows shortcut support is unavailable.");
        var shellObject = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Windows shortcut support is unavailable.");
        object? shortcutObject = null;
        try
        {
            dynamic shell = shellObject;
            shortcutObject = shell.CreateShortcut(path);
            dynamic shortcut = shortcutObject;
            shortcut.TargetPath = version.ExecutablePath;
            shortcut.WorkingDirectory = version.DirectoryPath;
            shortcut.IconLocation = version.ExecutablePath;
            shortcut.Description = $"Launch {version.DisplayName}";
            shortcut.Save();
        }
        finally
        {
            ReleaseComObject(shortcutObject);
            ReleaseComObject(shellObject);
        }
    }

    private static bool ShortcutTargets(string path, string executablePath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Version shortcuts are only supported on Windows.");

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows shortcut support is unavailable.");
        var shellObject = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Windows shortcut support is unavailable.");
        object? shortcutObject = null;
        try
        {
            dynamic shell = shellObject;
            shortcutObject = shell.CreateShortcut(path);
            dynamic shortcut = shortcutObject;
            var target = (string)shortcut.TargetPath;
            return !string.IsNullOrWhiteSpace(target)
                && Path.GetFullPath(target).Equals(
                    Path.GetFullPath(executablePath),
                    StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ReleaseComObject(shortcutObject);
            ReleaseComObject(shellObject);
        }
    }

    private static void MoveShortcut(string source, string destination)
    {
        if (source.Equals(destination, StringComparison.Ordinal))
            return;

        if (PathsEqual(source, destination))
        {
            var temporary = $"{source}.{Guid.NewGuid():N}.tmp";
            File.Move(source, temporary);
            try { File.Move(temporary, destination, overwrite: true); }
            catch
            {
                File.Move(temporary, source, overwrite: true);
                throw;
            }
            return;
        }

        File.Move(source, destination, overwrite: true);
    }

    private static bool PathsEqual(string first, string second) =>
        Path.GetFullPath(first).Equals(Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

    private static string GetShortcutPath(string directory, string displayName) =>
        TryGetShortcutPath(directory, displayName)
        ?? throw new ArgumentException("The version name cannot be used as a shortcut name.", nameof(displayName));

    private static string? TryGetShortcutPath(string directory, string displayName) =>
        IsValidDisplayName(displayName) ? Path.Combine(directory, $"{displayName}.lnk") : null;

    private static void ReleaseComObject(object? value)
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }

    private sealed record ShortcutMove(string Source, string Destination, string LocationName);
}
