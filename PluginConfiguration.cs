using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MediaShelf;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Base URL of the MediaShelf instance, e.g. https://mediashelf.example.com</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>Personal access token generated in MediaShelf under Settings → API.</summary>
    public string ApiToken { get; set; } = string.Empty;

    public bool SyncCollection { get; set; } = true;

    public bool SyncPlayback { get; set; } = true;

    public bool SyncRatings { get; set; } = true;

    /// <summary>Minimum seconds between playback-progress pushes for the same item.</summary>
    public int ProgressThrottleSeconds { get; set; } = 30;
}
