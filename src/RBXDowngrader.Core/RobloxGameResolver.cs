using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace RBXDowngrader.Core;

public sealed class RobloxGameResolver
{
    private readonly HttpClient _httpClient;

    public RobloxGameResolver(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string?> ResolveNameAsync(long placeId, CancellationToken cancellationToken = default)
    {
        if (placeId <= 0)
            throw new ArgumentOutOfRangeException(nameof(placeId));

        var universe = await _httpClient.GetFromJsonAsync<UniverseResponse>(
            $"https://apis.roblox.com/universes/v1/places/{placeId}/universe",
            cancellationToken).ConfigureAwait(false);
        if (universe?.UniverseId is not > 0)
            return null;

        var games = await _httpClient.GetFromJsonAsync<GamesResponse>(
            $"https://games.roblox.com/v1/games?universeIds={universe.UniverseId}",
            cancellationToken).ConfigureAwait(false);
        var name = games?.Data?.FirstOrDefault()?.Name?.Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private sealed record UniverseResponse(
        [property: JsonPropertyName("universeId")] long UniverseId);

    private sealed record GamesResponse(
        [property: JsonPropertyName("data")] GameDetails[]? Data);

    private sealed record GameDetails(
        [property: JsonPropertyName("name")] string Name);
}
