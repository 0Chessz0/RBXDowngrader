using System.Diagnostics;
using System.Text.Json;

namespace RBXDowngrader.Core;

public static class UpdateWorker
{
    public const string ApplyArgument = "--apply-update";
    public const string CleanupArgument = "--cleanup-update";
    public const string FailedArgument = "--update-failed";
    public const string PayloadManifestName = ".rbxdowngrader-app-files.json";

    private static readonly string UpdatesRoot = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "RBXDowngrader", "updates"));

    public static void ValidatePayload(string payloadDirectory)
    {
        var files = ReadPayloadManifest(payloadDirectory);
        if (!files.Contains("RBXDowngrader.exe", StringComparer.OrdinalIgnoreCase)
            || !files.Contains("uninstall.exe", StringComparer.OrdinalIgnoreCase)
            || !files.Contains(PayloadManifestName, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The update payload is incomplete.");
        }

        foreach (var relativePath in files)
        {
            ValidateRelativeApplicationPath(relativePath);
            if (!File.Exists(GetSafeChildPath(payloadDirectory, relativePath)))
                throw new InvalidDataException($"The update payload is missing {relativePath}.");
        }
    }

    public static async Task ApplyAsync(
        string installDirectory,
        string payloadDirectory,
        string updateRoot,
        int oldProcessId,
        CancellationToken cancellationToken = default)
    {
        installDirectory = Path.GetFullPath(installDirectory).TrimEnd(Path.DirectorySeparatorChar);
        payloadDirectory = Path.GetFullPath(payloadDirectory).TrimEnd(Path.DirectorySeparatorChar);
        updateRoot = Path.GetFullPath(updateRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (!IsChildOf(updateRoot, UpdatesRoot) || !IsChildOf(payloadDirectory, updateRoot)
            || !File.Exists(Path.Combine(installDirectory, ".install-scope")))
        {
            return;
        }

        try
        {
            ValidatePayload(payloadDirectory);
            await WaitForProcessExitAsync(oldProcessId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            StartFailureApplication(installDirectory);
            throw;
        }

        var backupRoot = Path.Combine(updateRoot, "backup");
        Directory.CreateDirectory(backupRoot);
        var newFiles = ReadPayloadManifest(payloadDirectory);
        var oldManifestPath = Path.Combine(installDirectory, PayloadManifestName);
        var oldFiles = File.Exists(oldManifestPath)
            ? ReadManifest(oldManifestPath)
            : Array.Empty<string>();
        var filesToReplace = newFiles
            .Concat(oldFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var movedFiles = new List<string>();
        var copiedFiles = new List<string>();

        try
        {
            foreach (var relativePath in filesToReplace)
            {
                ValidateRelativeApplicationPath(relativePath);
                var destination = GetSafeChildPath(installDirectory, relativePath);
                if (!File.Exists(destination))
                    continue;

                var backup = GetSafeChildPath(backupRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Move(destination, backup);
                movedFiles.Add(relativePath);
            }

            foreach (var relativePath in newFiles)
            {
                var source = GetSafeChildPath(payloadDirectory, relativePath);
                var destination = GetSafeChildPath(installDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: false);
                copiedFiles.Add(relativePath);
            }

            var updatedApp = Path.Combine(installDirectory, "RBXDowngrader.exe");
            var process = StartApplication(updatedApp, CleanupArgument, updateRoot, backupRoot)
                ?? throw new InvalidOperationException("The updated app could not be started.");
            _ = process.Id;
        }
        catch
        {
            RollBack(installDirectory, backupRoot, copiedFiles, movedFiles);
            StartFailureApplication(installDirectory);
            throw;
        }
    }

    public static async Task CleanupAsync(
        string updateRoot,
        string backupRoot,
        CancellationToken cancellationToken = default)
    {
        updateRoot = Path.GetFullPath(updateRoot).TrimEnd(Path.DirectorySeparatorChar);
        backupRoot = Path.GetFullPath(backupRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (!IsChildOf(updateRoot, UpdatesRoot) || !IsChildOf(backupRoot, updateRoot))
            return;

        for (var attempt = 0; attempt < 30 && Directory.Exists(updateRoot); attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Directory.Delete(updateRoot, recursive: true);
                return;
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static string[] ReadPayloadManifest(string payloadDirectory) =>
        ReadManifest(Path.Combine(payloadDirectory, PayloadManifestName));

    private static string[] ReadManifest(string manifestPath)
    {
        var files = JsonSerializer.Deserialize<string[]>(File.ReadAllText(manifestPath));
        if (files is null || files.Length == 0)
            throw new InvalidDataException("The update file manifest is invalid.");
        return files;
    }

    private static void ValidateRelativeApplicationPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException("The update contains an unsafe file path.");

        var normalized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
            throw new InvalidDataException("The update contains an unsafe file path.");

        if (segments[0].Equals("robloxversions", StringComparison.OrdinalIgnoreCase)
            || segments[0].Equals("temp", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("RBXDowngrader.log", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("recent-builds-cache.json", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(".install-scope", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The update attempted to replace application data.");
        }
    }

    private static string GetSafeChildPath(string root, string relativePath)
    {
        var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var childPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        if (!childPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update contains an unsafe file path.");
        return childPath;
    }

    private static void RollBack(
        string installDirectory,
        string backupRoot,
        IEnumerable<string> copiedFiles,
        IEnumerable<string> movedFiles)
    {
        foreach (var relativePath in copiedFiles.Reverse())
        {
            try { File.Delete(GetSafeChildPath(installDirectory, relativePath)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        foreach (var relativePath in movedFiles.Reverse())
        {
            var backup = GetSafeChildPath(backupRoot, relativePath);
            var destination = GetSafeChildPath(installDirectory, relativePath);
            if (!File.Exists(backup))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(backup, destination, overwrite: true);
        }
    }

    private static Process? StartApplication(string executable, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        return Process.Start(startInfo);
    }

    private static void StartFailureApplication(string installDirectory)
    {
        var restoredApp = Path.Combine(installDirectory, "RBXDowngrader.exe");
        if (!File.Exists(restoredApp))
            return;

        try
        {
            StartApplication(restoredApp, FailedArgument);
        }
        catch
        {
            // The original files remain intact even if relaunching is unavailable.
        }
    }

    private static async Task WaitForProcessExitAsync(int processId, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            // The original process already exited.
        }
    }

    private static bool IsChildOf(string target, string parent)
    {
        var parentPath = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var targetPath = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return targetPath.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase)
            && !targetPath.Equals(parentPath, StringComparison.OrdinalIgnoreCase);
    }
}
