using System.Net;
using System.Text.Json;

namespace RBXDowngrader.Core;

public sealed class RecentBuildService : IDisposable
{
    private static readonly Uri FeedUri = new("https://rbxoffsets.com/api/v1/windows/history?limit=5");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly string _cachePath;

    public RecentBuildService(HttpClient? httpClient = null, string? cachePath = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(AppIdentity.UserAgent);
        _cachePath = cachePath ?? AppPaths.RecentBuildsCache;
    }

    public async Task<RecentBuildResult> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(FeedUri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var builds = ParseBuilds(json);
            if (builds.Count == 0)
                throw new InvalidDataException("The recent-build feed contained no usable builds.");

            await TryWriteCacheAsync(json, cancellationToken).ConfigureAwait(false);
            return new RecentBuildResult(builds, IsCached: false);
        }
        catch (Exception ex) when (CanUseCache(ex, cancellationToken))
        {
            var cached = await TryReadCacheAsync(cancellationToken).ConfigureAwait(false);
            if (cached is not null)
                return new RecentBuildResult(cached, IsCached: true);
            throw;
        }
    }

    private static IReadOnlyList<RecentBuild> ParseBuilds(string json)
    {
        var payload = JsonSerializer.Deserialize<HistoryResponse>(json, JsonOptions);

        return (payload?.Versions ?? [])
            .Where(item => item.Installable && VersionHash.TryNormalize(item.Version, out _))
            .Take(5)
            .Select(item => new RecentBuild(
                item.Version,
                string.IsNullOrWhiteSpace(item.DisplayVersion) ? item.Version : item.DisplayVersion,
                item.LiveAt is > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(item.LiveAt.Value) : null,
                item.Recommended || string.Equals(item.Lifecycle, "live", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private async Task TryWriteCacheAsync(string json, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = _cachePath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, _cachePath, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<IReadOnlyList<RecentBuild>?> TryReadCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_cachePath))
                return null;

            var json = await File.ReadAllTextAsync(_cachePath, cancellationToken).ConfigureAwait(false);
            var builds = ParseBuilds(json);
            return builds.Count > 0 ? builds : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    private static bool CanUseCache(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or IOException or JsonException or InvalidDataException or NotSupportedException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }

    private sealed record HistoryResponse(HistoryVersion[] Versions);

    private sealed record HistoryVersion(
        string Version,
        string DisplayVersion,
        long? LiveAt,
        bool Installable,
        bool Recommended,
        string? Lifecycle);
}
