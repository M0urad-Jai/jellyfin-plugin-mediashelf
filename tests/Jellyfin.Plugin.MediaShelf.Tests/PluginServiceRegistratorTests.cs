using Jellyfin.Plugin.MediaShelf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.MediaShelf.Tests;

public class PluginServiceRegistratorTests
{
    [Fact]
    public void RegisterServices_resolves_MediaShelfClient()
    {
        // Regression for: AddHttpClient<MediaShelfClient>() was used in the DI
        // registration, which requires an HttpClient constructor parameter.
        // MediaShelfClient's constructor takes IHttpClientFactory, so DI
        // activation threw InvalidOperationException at plugin load.
        var sc = new ServiceCollection();
        sc.AddSingleton(NullLogger<PluginServiceRegistrator>.Instance);
        new PluginServiceRegistrator().RegisterServices(sc, applicationHost: null!);

        var sp = sc.BuildServiceProvider();
        var client = sp.GetService<MediaShelfClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void RegisterServices_resolves_IMemoryCache()
    {
        var sc = new ServiceCollection();
        new PluginServiceRegistrator().RegisterServices(sc, applicationHost: null!);

        var sp = sc.BuildServiceProvider();

        Assert.NotNull(sp.GetService<Microsoft.Extensions.Caching.Memory.IMemoryCache>());
    }
}
