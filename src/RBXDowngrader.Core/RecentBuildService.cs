using System.Net;
using System.Text.Json;

namespace RBXDowngrader.Core;

public sealed class RecentBuildService : IDisposable
{
    private static readonly Uri FeedUri = new("https://rbxoffsets.com/api/v1/windows/history?limit=5");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public RecentBuildService(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("RBXDowngrader/1.2");
    }

    public async Task<IReadOnlyList<RecentBuild>> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(FeedUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<HistoryResponse>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

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
