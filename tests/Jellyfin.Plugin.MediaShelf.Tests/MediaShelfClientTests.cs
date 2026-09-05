using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Jellyfin.Plugin.MediaShelf;

namespace Jellyfin.Plugin.MediaShelf.Tests;

public class MediaShelfClientTests
{
    [Fact]
    public async Task AddToCollectionAsync_sets_bearer_authorization()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = new MediaShelfClient(new StubFactory(handler), NullLogger<MediaShelfClient>.Instance);

        await client.AddToCollectionAsync("https://x.test", "secret-token", "movies",
            new JsonObject { ["title"] = "X" });

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("secret-token", handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task AddToCollectionAsync_trims_trailing_slash_from_server_url()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = new MediaShelfClient(new StubFactory(handler), NullLogger<MediaShelfClient>.Instance);

        await client.AddToCollectionAsync("https://x.test/", "t", "movies",
            new JsonObject { ["title"] = "X" });

        Assert.Equal("https://x.test/api/v1/sync/collection", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task AddToCollectionAsync_posts_collection_wrapped_array()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = new MediaShelfClient(new StubFactory(handler), NullLogger<MediaShelfClient>.Instance);

        await client.AddToCollectionAsync("https://x.test", "t", "movies",
            new JsonObject { ["title"] = "X" });

        var body = JsonNode.Parse(handler.LastBody!);
        Assert.NotNull(body!["movies"]);
        Assert.IsType<JsonArray>(body["movies"]);
        Assert.Single((JsonArray)body["movies"]!);
    }

    [Fact]
    public async Task AddToCollectionAsync_skips_when_url_blank()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = new MediaShelfClient(new StubFactory(handler), NullLogger<MediaShelfClient>.Instance);

        await client.AddToCollectionAsync("", "t", "movies", new JsonObject());

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task AddToCollectionAsync_skips_when_token_blank()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = new MediaShelfClient(new StubFactory(handler), NullLogger<MediaShelfClient>.Instance);

        await client.AddToCollectionAsync("https://x.test", "", "movies", new JsonObject());

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task AddToCollectionAsync_logs_warning_on_non_2xx_with_body()
    {
        var logger = new ListLogger<MediaShelfClient>();
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":\"bad\"}", Encoding.UTF8, "application/json"),
        });
        var client = new MediaShelfClient(new StubFactory(handler), logger);

        await client.AddToCollectionAsync("https://x.test", "t", "movies", new JsonObject());

        var warning = logger.Entries.SingleOrDefault(e => e.Level == LogLevel.Warning);
        Assert.NotNull(warning);
        Assert.Contains("400", warning!.Message);
        Assert.Contains("\"error\":\"bad\"", warning.Message);
    }

    [Fact]
    public async Task AddToCollectionAsync_swallows_network_exception()
    {
        var logger = new ListLogger<MediaShelfClient>();
        var handler = new ThrowingHandler(new HttpRequestException("network down"));
        var client = new MediaShelfClient(new StubFactory(handler), logger);

        // Must not throw.
        await client.AddToCollectionAsync("https://x.test", "t", "movies", new JsonObject());

        var error = logger.Entries.SingleOrDefault(e => e.Level == LogLevel.Error);
        Assert.NotNull(error);
        Assert.IsType<HttpRequestException>(error!.Exception);
    }

    [Fact]
    public async Task RateAsync_posts_ratings_array_for_resource()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = new MediaShelfClient(new StubFactory(handler), NullLogger<MediaShelfClient>.Instance);

        await client.RateAsync("https://x.test", "t", "movies",
            new JsonObject { ["rating"] = 4.0 });

        var body = JsonNode.Parse(handler.LastBody!);
        Assert.NotNull(body!["movies"]);
        Assert.IsType<JsonArray>(body["movies"]);
    }

    [Fact]
    public async Task UpdatePlaybackAsync_adds_type_key_to_body()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = new MediaShelfClient(new StubFactory(handler), NullLogger<MediaShelfClient>.Instance);

        await client.UpdatePlaybackAsync("https://x.test", "t", "movies",
            new JsonObject { ["progress"] = 50.0 });

        var body = JsonNode.Parse(handler.LastBody!);
        Assert.Equal("movies", (string?)body!["type"]);
        Assert.Equal(50.0, (double?)body["progress"]);
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public int RequestCount { get; private set; }

        public CapturingHandler(HttpResponseMessage response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            RequestCount++;
            return _response;
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _ex;
        public ThrowingHandler(Exception ex) => _ex = ex;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw _ex;
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
