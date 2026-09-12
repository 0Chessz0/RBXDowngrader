using System.Diagnostics;
using System.Text.Json;

namespace RBXDowngrader.Core;

public sealed class VersionStore
{
    private const string MetadataFileName = ".rbxdowngrader.json";
    private const string DeleteMarkerFileName = ".delete-pending";
    public const string DeleteWorkerArgument = "--delete-version";

    private static string PendingDeletesRoot => Path.Combine(AppPaths.Temp, "pending-delete");
    private readonly VersionShortcutService _shortcuts;

    public VersionStore(VersionShortcutService? shortcuts = null)
    {
        _shortcuts = shortcuts ?? new VersionShortcutService();
    }

    public async Task<IReadOnlyList<InstalledVersion>> GetInstalledAsync(CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureCreated();
        var results = new List<InstalledVersion>();

        foreach (var directory in Directory.EnumerateDirectories(AppPaths.Versions, "version-*"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(Path.Combine(directory, DeleteMarkerFileName)))
                continue;

            var executable = FindPlayerExecutable(directory);
            if (executable is null)
                continue;

            var metadata = await ReadMetadataAsync(directory, cancellationToken).ConfigureAwait(false);
            var installedAt = metadata?.InstalledAt ?? Directory.GetCreationTimeUtc(directory);
            var size = await Task.Run(() => GetDirectorySize(directory, cancellationToken), cancellationToken)
                .ConfigureAwait(false);

            var installedVersion = new InstalledVersion(
                Path.GetFileName(directory), directory, installedAt, size, executable, metadata?.CustomName);
            try { _shortcuts.UpgradeExisting(installedVersion); }
            catch { }
            results.Add(installedVersion);
        }

        return results.OrderByDescending(version => version.InstalledAt).ToArray();
    }

    public void Launch(InstalledVersion version, string? arguments = null)
    {
        if (!File.Exists(version.ExecutablePath))
            throw new FileNotFoundException("RobloxPlayerBeta.exe is missing.", version.ExecutablePath);

        Process.Start(new ProcessStartInfo
        {
            FileName = version.ExecutablePath,
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = version.DirectoryPath,
            UseShellExecute = true
        });
    }

    public void LaunchPrivateServer(InstalledVersion version, string link)
    {
        if (!File.Exists(version.ExecutablePath))
            throw new FileNotFoundException("RobloxPlayerBeta.exe is missing.", version.ExecutablePath);
        if (!PrivateServerLink.TryNormalize(link, out var normalizedLink))
            throw new ArgumentException("Enter a valid Roblox private server link.", nameof(link));

        var startInfo = new ProcessStartInfo
        {
            FileName = version.ExecutablePath,
            WorkingDirectory = version.DirectoryPath,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--deeplink");
        startInfo.ArgumentList.Add(normalizedLink);
        Process.Start(startInfo);
    }

    public void QueueDelete(InstalledVersion version)
    {
        AppPaths.EnsureCreated();
        if (!IsChildOf(version.DirectoryPath, AppPaths.Versions))
            throw new InvalidOperationException("Refusing to delete a folder outside the versions directory.");

        _shortcuts.Remove(version);
        if (!Directory.Exists(version.DirectoryPath))
            return;

        Directory.CreateDirectory(PendingDeletesRoot);
        var pendingPath = Path.Combine(
            PendingDeletesRoot,
            $"{Path.GetFileName(version.DirectoryPath)}-{Guid.NewGuid():N}");

        string workerTarget;
        try
        {
            Directory.Move(version.DirectoryPath, pendingPath);
            workerTarget = pendingPath;
        }
        catch (IOException)
        {
            File.WriteAllText(Path.Combine(version.DirectoryPath, DeleteMarkerFileName), string.Empty);
            workerTarget = version.DirectoryPath;
        }
        catch (UnauthorizedAccessException)
        {
            File.WriteAllText(Path.Combine(version.DirectoryPath, DeleteMarkerFileName), string.Empty);
            workerTarget = version.DirectoryPath;
        }

        StartDeleteWorker(workerTarget);
    }

    public async Task<InstalledVersion> RenameAsync(
        InstalledVersion version,
        string name,
        CancellationToken cancellationToken = default)
    {
        if (!IsChildOf(version.DirectoryPath, AppPaths.Versions) || !Directory.Exists(version.DirectoryPath))
            throw new InvalidOperationException("That version is no longer installed.");

        var normalizedName = name.Trim();
        if (normalizedName.Length is < 1 or > 40 || normalizedName.Any(char.IsControl))
            throw new ArgumentException("Use a name between 1 and 40 characters.", nameof(name));
        if (!VersionShortcutService.IsValidDisplayName(normalizedName))
            throw new ArgumentException("The name contains characters Windows cannot use in a shortcut.", nameof(name));

        var renamed = version with { CustomName = normalizedName };
        await WriteMetadataAsync(
            version.DirectoryPath,
            new VersionMetadata(version.Version, version.InstalledAt, normalizedName),
            cancellationToken).ConfigureAwait(false);
        try
        {
            _shortcuts.RenameExisting(version, renamed);
        }
        catch
        {
            await WriteMetadataAsync(
                version.DirectoryPath,
                new VersionMetadata(version.Version, version.InstalledAt, version.CustomName),
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        return renamed;
    }

    public VersionShortcutState GetShortcutState(InstalledVersion version) => _shortcuts.GetState(version);

    public void SetShortcutState(InstalledVersion version, bool desktop, bool startMenu) =>
        _shortcuts.SetState(version, desktop, startMenu);

    public static Task WriteMetadataAsync(string directory, string version, CancellationToken cancellationToken) =>
        WriteMetadataAsync(directory, new VersionMetadata(version, DateTimeOffset.UtcNow, null), cancellationToken);

    public static void ResumePendingDeletes()
    {
        AppPaths.EnsureCreated();
        Directory.CreateDirectory(PendingDeletesRoot);

        foreach (var directory in Directory.EnumerateDirectories(PendingDeletesRoot).ToArray())
            StartDeleteWorker(directory);

        foreach (var directory in Directory.EnumerateDirectories(AppPaths.Versions, "version-*").ToArray())
        {
            if (File.Exists(Path.Combine(directory, DeleteMarkerFileName)))
                StartDeleteWorker(directory);
        }
    }

    public static async Task RunDeleteWorkerAsync(string target, CancellationToken cancellationToken = default)
    {
        if (!IsAllowedDeleteTarget(target))
            return;

        var deadline = DateTimeOffset.UtcNow.AddHours(24);
        while (Directory.Exists(target) && DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Directory.Delete(target, recursive: true);
                return;
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<VersionMetadata?> ReadMetadataAsync(string directory, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, MetadataFileName);
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<VersionMetadata>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task WriteMetadataAsync(
        string directory,
        VersionMetadata metadata,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, MetadataFileName);
        var temporaryPath = path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, metadata, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static void StartDeleteWorker(string target)
    {
        // Recursive deletion can be blocked by Roblox or antivirus file locks. Relaunching
        // this executable as a worker lets the main window close immediately and keeps retrying
        // independently after the user exits the launcher, without holding UI state in memory.
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            throw new InvalidOperationException("Could not start the background delete worker.");

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add(DeleteWorkerArgument);
        startInfo.ArgumentList.Add(target);
        Process.Start(startInfo);
    }

    private static bool IsAllowedDeleteTarget(string target)
    {
        if (IsChildOf(target, PendingDeletesRoot))
            return true;

        return IsChildOf(target, AppPaths.Versions)
            && File.Exists(Path.Combine(target, DeleteMarkerFileName));
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

    private static string? FindPlayerExecutable(string directory)
    {
        var direct = Path.Combine(directory, "RobloxPlayerBeta.exe");
        if (File.Exists(direct))
            return direct;

        return Directory.EnumerateFiles(directory, "RobloxPlayerBeta.exe", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private static long GetDirectorySize(string directory, CancellationToken cancellationToken)
    {
        long size = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { size += new FileInfo(file).Length; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return size;
    }

    private sealed record VersionMetadata(string Version, DateTimeOffset InstalledAt, string? CustomName);
}
