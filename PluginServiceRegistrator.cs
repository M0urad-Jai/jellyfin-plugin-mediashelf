using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.MediaShelf;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddMemoryCache();
        // Register IHttpClientFactory (parameterless AddHttpClient) so the typed
        // client below can resolve it. Don't use AddHttpClient<MediaShelfClient>():
        // that requires an HttpClient constructor parameter, which MediaShelfClient
        // doesn't have, and would throw at activation. The per-request timeout is
        // enforced inside MediaShelfClient via CancellationTokenSource.
        serviceCollection.AddHttpClient();
        serviceCollection.AddTransient<MediaShelfClient>();
        serviceCollection.AddHostedService<SyncService>();
    }
}
