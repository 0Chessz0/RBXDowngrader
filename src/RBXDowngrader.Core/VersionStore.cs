using System.Diagnostics;
using System.Text.Json;

namespace RBXDowngrader.Core;

public sealed class VersionStore
{
    private const string MetadataFileName = ".rbxdowngrader.json";

    public async Task<IReadOnlyList<InstalledVersion>> GetInstalledAsync(CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureCreated();
        var results = new List<InstalledVersion>();

        foreach (var directory in Directory.EnumerateDirectories(AppPaths.Versions, "version-*"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var executable = FindPlayerExecutable(directory);
            if (executable is null)
                continue;

            var metadata = await ReadMetadataAsync(directory, cancellationToken).ConfigureAwait(false);
            var installedAt = metadata?.InstalledAt ?? Directory.GetCreationTimeUtc(directory);
            var size = await Task.Run(() => GetDirectorySize(directory, cancellationToken), cancellationToken)
                .ConfigureAwait(false);

            results.Add(new InstalledVersion(
                Path.GetFileName(directory), directory, installedAt, size, executable));
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

    public async Task DeleteAsync(InstalledVersion version, CancellationToken cancellationToken = default)
    {
        var versionsRoot = Path.GetFullPath(AppPaths.Versions)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(version.DirectoryPath)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!target.StartsWith(versionsRoot, StringComparison.OrdinalIgnoreCase) || target == versionsRoot)
            throw new InvalidOperationException("Refusing to delete a folder outside the versions directory.");

        await Task.Run(() => Directory.Delete(version.DirectoryPath, recursive: true), cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task WriteMetadataAsync(string directory, string version, CancellationToken cancellationToken)
    {
        var metadata = new VersionMetadata(version, DateTimeOffset.UtcNow);
        await using var stream = File.Create(Path.Combine(directory, MetadataFileName));
        await JsonSerializer.SerializeAsync(stream, metadata, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
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

    private sealed record VersionMetadata(string Version, DateTimeOffset InstalledAt);
}
