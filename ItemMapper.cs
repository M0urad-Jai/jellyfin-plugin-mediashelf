using System.Text.Json.Nodes;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.MediaShelf;

public static class ItemMapper
{
    // Jellyfin provider id key -> MediaShelf provider key.
    private static readonly (string Jellyfin, string MediaShelf)[] IdMap =
    {
        ("Tmdb", "tmdb"),
        ("Imdb", "imdb"),
        ("Tvdb", "tvdb"),
        ("AniList", "anilist"),
        ("MyAnimeList", "mal"),
        ("MusicBrainzReleaseGroup", "musicbrainz"),
    };

    public static JsonObject ToEntry(BaseItem item)
    {
        var ids = new JsonObject();
        foreach (var (jellyfinKey, mediaShelfKey) in IdMap)
        {
            if (item.ProviderIds.TryGetValue(jellyfinKey, out var value) && !string.IsNullOrEmpty(value))
            {
                ids[mediaShelfKey] = value;
            }
        }

        var entry = new JsonObject
        {
            ["title"] = item.Name,
            ["ids"] = ids,
        };
        if (item.ProductionYear.HasValue)
        {
            entry["year"] = item.ProductionYear.Value;
        }

        return entry;
    }

    /// <summary>Resource key MediaShelf's API groups by: "movies", "shows", etc.</summary>
    public static string? ResourceFor(BaseItem item) => item switch
    {
        Movie => "movies",
        Series => "shows",
        _ => null,
    };

    /// <summary>True if any provider id has been populated. Used to defer collection sync until metadata providers run.</summary>
    public static bool HasProviderIds(BaseItem item) => item.ProviderIds.Count > 0;
}
