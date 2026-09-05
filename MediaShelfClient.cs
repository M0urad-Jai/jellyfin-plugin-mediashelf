using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaShelf;

/// <summary>Thin wrapper around MediaShelf's /api/v1/sync/* endpoints.</summary>
public class MediaShelfClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    public MediaShelfClient(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task AddToCollectionAsync(string resource, JsonObject entry) =>
        PostAsync("/api/v1/sync/collection", new JsonObject { [resource] = new JsonArray(entry) });

    public Task AddHistoryAsync(string resource, JsonObject entry) =>
        PostAsync("/api/v1/sync/history", new JsonObject { [resource] = new JsonArray(entry) });

    public Task UpdatePlaybackAsync(string resource, JsonObject entry)
    {
        entry["type"] = resource;
        return PostAsync("/api/v1/sync/playback", entry);
    }

    public Task RateAsync(string resource, JsonObject entry) =>
        PostAsync("/api/v1/sync/ratings", new JsonObject { [resource] = new JsonArray(entry) });

    private async Task PostAsync(string path, JsonNode body)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || string.IsNullOrWhiteSpace(config.ServerUrl) || string.IsNullOrWhiteSpace(config.ApiToken))
        {
            _logger.LogDebug("MediaShelf: not configured, skipping {Path}", path);
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, config.ServerUrl.TrimEnd('/') + path)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiToken);

            using var response = await client.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                _logger.LogWarning("MediaShelf {Path} returned {Status}: {Body}", path, response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MediaShelf: request to {Path} failed", path);
        }
    }
}
