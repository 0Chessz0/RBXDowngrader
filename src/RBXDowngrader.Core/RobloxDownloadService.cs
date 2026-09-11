using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;

namespace RBXDowngrader.Core;

public sealed class RobloxDownloadService : IDisposable
{
    private const int MaxPackageDownloadAttempts = 3;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(2);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly string _versionsDirectory;
    private readonly string _tempDirectory;
    private readonly string _logFile;

    public RobloxDownloadService(HttpClient? httpClient = null)
        : this(
            httpClient ?? new HttpClient(CreateDefaultHandler()),
            ownsClient: httpClient is null,
            AppPaths.Versions,
            AppPaths.Temp,
            AppPaths.LogFile)
    {
    }

    public RobloxDownloadService(HttpMessageHandler handler, string storageRoot)
        : this(
            new HttpClient(handler ?? throw new ArgumentNullException(nameof(handler))),
            ownsClient: true,
            Path.Combine(storageRoot, "robloxversions"),
            Path.Combine(storageRoot, "temp"),
            Path.Combine(storageRoot, "RBXDowngrader.log"))
    {
    }

    private RobloxDownloadService(
        HttpClient httpClient,
        bool ownsClient,
        string versionsDirectory,
        string tempDirectory,
        string logFile)
    {
        _ownsClient = ownsClient;
        _httpClient = httpClient;
        _versionsDirectory = versionsDirectory;
        _tempDirectory = tempDirectory;
        _logFile = logFile;
        _httpClient.Timeout = TimeSpan.FromMinutes(30);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(AppIdentity.UserAgent);
    }

    public async Task<string> DownloadAsync(
        string versionInput,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!VersionHash.TryNormalize(versionInput, out var version))
            throw new ArgumentException("Enter a valid 16 character Roblox version hash.", nameof(versionInput));

        Directory.CreateDirectory(_versionsDirectory);
        Directory.CreateDirectory(_tempDirectory);
        var destination = Path.Combine(_versionsDirectory, version);
        if (Directory.Exists(destination))
            throw new InvalidOperationException("That version is already installed.");

        var staging = Path.Combine(_tempDirectory, $"{version}-{Guid.NewGuid():N}");
        var downloads = Path.Combine(staging, "packages");
        var assembled = Path.Combine(staging, "assembled");
        Directory.CreateDirectory(downloads);
        Directory.CreateDirectory(assembled);

        try
        {
            progress?.Report(new DownloadProgress("Checking version", 0));
            var (baseUri, manifestText) = await FetchManifestAsync(version, cancellationToken)
                .ConfigureAwait(false);
            var packages = PackageManifest.Parse(manifestText);
            if (packages.Count == 0)
                throw new InvalidDataException("The deployment contains no packages.");

            var expectedBytes = packages.Sum(package => package.CompressedSize);
            long receivedBytes = 0;
            var completed = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var limiter = new SemaphoreSlim(4);

            var tasks = packages.Select(async package =>
            {
                await limiter.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var path = Path.Combine(downloads, package.Name);
                    await DownloadPackageWithRetryAsync(
                        new Uri(baseUri, $"{version}-{package.Name}"),
                        path,
                        package.Checksum,
                        package.Name,
                        bytes =>
                        {
                            var total = Interlocked.Add(ref receivedBytes, bytes);
                            var percentage = expectedBytes > 0
                                ? Math.Min(78, total * 78d / expectedBytes)
                                : 0;
                            progress?.Report(new DownloadProgress(
                                $"Downloading {package.Name}", percentage, total, expectedBytes));
                        },
                        progress,
                        cancellationToken).ConfigureAwait(false);
                    completed[package.Name] = path;
                }
                finally
                {
                    limiter.Release();
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);

            for (var index = 0; index < packages.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var package = packages[index];
                progress?.Report(new DownloadProgress(
                    $"Installing {package.Name}",
                    78 + ((index + 1d) / packages.Count * 20),
                    receivedBytes,
                    expectedBytes));

                if (package.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var extractionPath = Path.Combine(assembled, PackageMap.GetExtractionDirectory(package.Name));
                    SafeZipExtractor.Extract(completed[package.Name], extractionPath);
                }
                else
                {
                    File.Copy(completed[package.Name], Path.Combine(assembled, package.Name), overwrite: true);
                }
            }

            var player = Path.Combine(assembled, "RobloxPlayerBeta.exe");
            if (!File.Exists(player))
                throw new InvalidDataException("The downloaded build does not contain RobloxPlayerBeta.exe.");

            await File.WriteAllTextAsync(
                Path.Combine(assembled, "AppSettings.xml"),
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Settings><ContentFolder>content</ContentFolder><BaseUrl>https://www.roblox.com</BaseUrl></Settings>",
                cancellationToken).ConfigureAwait(false);
            await VersionStore.WriteMetadataAsync(assembled, version, cancellationToken).ConfigureAwait(false);

            Directory.Move(assembled, destination);
            progress?.Report(new DownloadProgress("Ready", 100, receivedBytes, expectedBytes));
            return destination;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await AppendLogAsync(version, ex).ConfigureAwait(false);
            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task<(Uri BaseUri, string Manifest)> FetchManifestAsync(
        string version,
        CancellationToken cancellationToken)
    {
        Uri[] candidates =
        [
            new("https://setup.rbxcdn.com/"),
            new("https://setup-aws.rbxcdn.com/"),
            new("https://setup.rbxcdn.com/channel/common/")
        ];

        foreach (var candidate in candidates)
        {
            using var response = await _httpClient.GetAsync(
                new Uri(candidate, $"{version}-rbxPkgManifest.txt"),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
                continue;

            response.EnsureSuccessStatusCode();
            return (candidate, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }

        throw new InvalidOperationException("That version could not be found on Roblox's deployment servers.");
    }

    private async Task DownloadPackageAsync(
        Uri uri,
        string destination,
        string checksum,
        Action<long> reportBytes,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var target = new FileStream(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, useAsync: true))
        {
            var buffer = new byte[81_920];
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                reportBytes(read);
            }
        }

        if (checksum.Length == 32 && checksum.All(Uri.IsHexDigit))
        {
            // The MD5 value comes from Roblox's deployment manifest and checks package
            // integrity over HTTPS. It is not a digital signature or publisher-authenticity proof.
            await using var file = File.OpenRead(destination);
            var actual = Convert.ToHexString(await MD5.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
            if (!actual.Equals(checksum, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Integrity check failed for {Path.GetFileName(destination)}.");
        }
    }

    private async Task DownloadPackageWithRetryAsync(
        Uri uri,
        string destination,
        string checksum,
        string packageName,
        Action<long> reportBytes,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var retryDelay = InitialRetryDelay;
        for (var attempt = 1; attempt <= MaxPackageDownloadAttempts; attempt++)
        {
            long attemptBytes = 0;
            try
            {
                await DownloadPackageAsync(
                    uri,
                    destination,
                    checksum,
                    bytes =>
                    {
                        attemptBytes += bytes;
                        reportBytes(bytes);
                    },
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (IsTransientDownloadFailure(ex, cancellationToken))
            {
                if (attemptBytes > 0)
                    reportBytes(-attemptBytes);
                TryDeletePartialDownload(destination);

                if (attempt == MaxPackageDownloadAttempts)
                    throw;

                progress?.Report(new DownloadProgress(
                    $"Retrying {packageName}", 0));
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                retryDelay = TimeSpan.FromMilliseconds(Math.Min(
                    retryDelay.TotalMilliseconds * 2,
                    MaximumRetryDelay.TotalMilliseconds));
            }
        }
    }

    private static bool IsTransientDownloadFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or IOException or TimeoutException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static void TryDeletePartialDownload(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task AppendLogAsync(string version, Exception exception)
    {
        try
        {
            var logDirectory = Path.GetDirectoryName(_logFile);
            if (!string.IsNullOrWhiteSpace(logDirectory))
                Directory.CreateDirectory(logDirectory);
            await File.AppendAllTextAsync(
                _logFile,
                $"[{DateTimeOffset.Now:O}] {version}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}")
                .ConfigureAwait(false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static HttpMessageHandler CreateDefaultHandler() => new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }
}
