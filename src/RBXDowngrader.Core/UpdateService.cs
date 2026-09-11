using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RBXDowngrader.Core;

public sealed class UpdateService : IDisposable
{
    private static readonly Uri LatestReleaseUri = new(
        "https://api.github.com/repos/0Chessz0/RBXDowngrader/releases/latest");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly string _updatesRoot;
    private readonly Version _currentVersion;
    private readonly Func<string, CancellationToken, Task<Stream>> _payloadStreamFactory;

    public UpdateService(
        HttpClient? httpClient = null,
        string? updatesRoot = null,
        Version? currentVersion = null,
        Func<string, CancellationToken, Task<Stream>>? payloadStreamFactory = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(AppIdentity.UserAgent);
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
        _updatesRoot = updatesRoot ?? Path.Combine(Path.GetTempPath(), "RBXDowngrader", "updates");
        _currentVersion = currentVersion ?? AppIdentity.Version;
        _payloadStreamFactory = payloadStreamFactory ?? OpenEmbeddedPayloadAsync;
    }

    public async Task<UpdateRelease?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseUri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
            stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        if (release is null || !TryParseReleaseVersion(release.TagName, out var availableVersion)
            || availableVersion <= _currentVersion)
        {
            return null;
        }

        var expectedAssetName = $"RBXDowngraderSetup-{GetRuntimeIdentifier()}.exe";
        var asset = release.Assets.FirstOrDefault(candidate =>
            candidate.Name.Equals(expectedAssetName, StringComparison.OrdinalIgnoreCase));
        if (asset is null || asset.Size <= 0 || !TryParseSha256(asset.Digest, out var sha256)
            || !Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out var downloadUri)
            || downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return new UpdateRelease(
            availableVersion,
            release.TagName,
            asset.Name,
            downloadUri,
            asset.Size,
            sha256,
            release.Body ?? string.Empty);
    }

    public async Task<PreparedUpdate> PrepareAsync(
        UpdateRelease release,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var updateRoot = Path.Combine(_updatesRoot, $"{release.AvailableVersion}-{Guid.NewGuid():N}");
        var installerPath = Path.Combine(updateRoot, release.AssetName);
        var payloadArchivePath = Path.Combine(updateRoot, "Payload.zip");
        var payloadDirectory = Path.Combine(updateRoot, "payload");
        Directory.CreateDirectory(updateRoot);

        try
        {
            using var response = await _httpClient.GetAsync(
                release.AssetUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is long contentLength
                && contentLength != release.AssetSize)
            {
                throw new InvalidDataException("The update size does not match its release metadata.");
            }

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(
                installerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, useAsync: true))
            {
                var buffer = new byte[81_920];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    received += read;
                    if (received > release.AssetSize)
                        throw new InvalidDataException("The update is larger than its release metadata.");
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    progress?.Report(received * 100d / release.AssetSize);
                }
            }

            var fileInfo = new FileInfo(installerPath);
            if (fileInfo.Length != release.AssetSize)
                throw new InvalidDataException("The update did not download completely.");

            await using (var installer = File.OpenRead(installerPath))
            {
                var actualHash = Convert.ToHexString(
                    await SHA256.HashDataAsync(installer, cancellationToken).ConfigureAwait(false));
                if (!actualHash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The update hash does not match its release metadata.");
            }

            await using (var payload = await _payloadStreamFactory(installerPath, cancellationToken).ConfigureAwait(false))
            await using (var payloadArchive = new FileStream(
                payloadArchivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, useAsync: true))
            {
                await payload.CopyToAsync(payloadArchive, cancellationToken).ConfigureAwait(false);
            }

            SafeZipExtractor.Extract(payloadArchivePath, payloadDirectory);
            UpdateWorker.ValidatePayload(payloadDirectory);
            return new PreparedUpdate(release, updateRoot, payloadDirectory);
        }
        catch
        {
            TryDeleteDirectory(updateRoot);
            throw;
        }
    }

    public void StartWorker(PreparedUpdate update, string installDirectory, int processId)
    {
        UpdateWorker.ValidatePayload(update.PayloadDirectory);
        var workerPath = Path.Combine(update.PayloadDirectory, "RBXDowngrader.exe");
        var scopePath = Path.Combine(installDirectory, ".install-scope");
        if (!File.Exists(scopePath))
            throw new InvalidOperationException("Self-update is only available for installed copies.");

        var allUsers = File.ReadAllText(scopePath).Trim()
            .Equals("all-users", StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo
        {
            FileName = workerPath,
            WorkingDirectory = update.PayloadDirectory,
            UseShellExecute = true
        };
        if (allUsers)
            startInfo.Verb = "runas";
        startInfo.ArgumentList.Add(UpdateWorker.ApplyArgument);
        startInfo.ArgumentList.Add(Path.GetFullPath(installDirectory));
        startInfo.ArgumentList.Add(Path.GetFullPath(update.PayloadDirectory));
        startInfo.ArgumentList.Add(Path.GetFullPath(update.UpdateRoot));
        startInfo.ArgumentList.Add(processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the update worker.");
    }

    private static bool TryParseReleaseVersion(string tagName, out Version version) =>
        Version.TryParse(tagName.Trim().TrimStart('v', 'V'), out version!);

    private static bool TryParseSha256(string? digest, out string sha256)
    {
        sha256 = string.Empty;
        if (digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            return false;

        var candidate = digest[7..];
        if (candidate.Length != 64 || !candidate.All(Uri.IsHexDigit))
            return false;
        sha256 = candidate;
        return true;
    }

    private static string GetRuntimeIdentifier() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "win-arm64",
        _ => "win-x64"
    };

    private static async Task<Stream> OpenEmbeddedPayloadAsync(
        string installerPath,
        CancellationToken cancellationToken)
    {
        var payloadPath = installerPath + ".payload.zip";
        var startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            WorkingDirectory = Path.GetDirectoryName(installerPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("--extract-payload");
        startInfo.ArgumentList.Add(payloadPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidDataException("The downloaded installer could not be started.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0 || !File.Exists(payloadPath))
            throw new InvalidDataException("The downloaded installer did not provide an application payload.");

        return new FileStream(
            payloadPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81_920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("assets")] GitHubAsset[] Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("digest")] string? Digest);
}
