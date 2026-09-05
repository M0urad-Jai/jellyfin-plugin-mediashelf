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
    private readonly ILogger<MediaShelfClient> _logger;

    public MediaShelfClient(IHttpClientFactory httpClientFactory, ILogger<MediaShelfClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task AddToCollectionAsync(string serverUrl, string apiToken, string resource, JsonObject entry) =>
        PostAsync(serverUrl, apiToken, "/api/v1/sync/collection", new JsonObject { [resource] = new JsonArray(entry) });

    public Task AddHistoryAsync(string serverUrl, string apiToken, string resource, JsonObject entry) =>
        PostAsync(serverUrl, apiToken, "/api/v1/sync/history", new JsonObject { [resource] = new JsonArray(entry) });

    public Task UpdatePlaybackAsync(string serverUrl, string apiToken, string resource, JsonObject entry)
    {
        entry["type"] = resource;
        return PostAsync(serverUrl, apiToken, "/api/v1/sync/playback", entry);
    }

    public Task RateAsync(string serverUrl, string apiToken, string resource, JsonObject entry) =>
        PostAsync(serverUrl, apiToken, "/api/v1/sync/ratings", new JsonObject { [resource] = new JsonArray(entry) });

    private async Task PostAsync(string serverUrl, string apiToken, string path, JsonNode body)
    {
        if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(apiToken))
        {
            _logger.LogDebug("MediaShelf: not configured, skipping {Path}", path);
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, serverUrl.TrimEnd('/') + path)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

            using var response = await client.SendAsync(request, cts.Token).ConfigureAwait(false);
            // Cast to int: HttpStatusCode.ToString() returns the name (e.g. "BadRequest"),
            // and a numeric status is what operators expect to see in the log.
            var status = (int)response.StatusCode;
            _logger.LogDebug("MediaShelf {Path} -> {Status}", path, status);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                _logger.LogWarning("MediaShelf {Path} returned {Status}: {Body}", path, status, responseBody);
            }
        }
        catch (OperationCanceledException)
        {
            // Most likely MediaShelf is unreachable; this is not a server-side error.
            _logger.LogWarning("MediaShelf {Path} timed out", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MediaShelf: request to {Path} failed", path);
        }
    }
}
