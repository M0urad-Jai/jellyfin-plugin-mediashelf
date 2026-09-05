using System.Text.Json.Nodes;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Jellyfin.Plugin.MediaShelf;

namespace Jellyfin.Plugin.MediaShelf.Tests;

public class ItemMapperTests
{
    [Fact]
    public void ToEntry_movie_with_tmdb_id_emits_tmdb_key()
    {
        var movie = new Movie { Name = "The Matrix", ProviderIds = { ["Tmdb"] = "603" } };

        var entry = ItemMapper.ToEntry(movie);

        Assert.Equal("The Matrix", (string?)entry["title"]);
        Assert.Equal("603", (string?)entry["ids"]!["tmdb"]);
    }

    [Fact]
    public void ToEntry_series_with_imdb_id_emits_imdb_key()
    {
        var series = new Series { Name = "Breaking Bad", ProviderIds = { ["Imdb"] = "tt0903747" } };

        var entry = ItemMapper.ToEntry(series);

        Assert.Equal("tt0903747", (string?)entry["ids"]!["imdb"]);
    }

    [Fact]
    public void ToEntry_aniList_id_uses_anilist_key()
    {
        var series = new Series { Name = "Cowboy Bebop", ProviderIds = { ["AniList"] = "23" } };

        var entry = ItemMapper.ToEntry(series);

        Assert.Equal("23", (string?)entry["ids"]!["anilist"]);
    }

    [Fact]
    public void ToEntry_empty_provider_ids_yields_empty_ids_object()
    {
        var movie = new Movie { Name = "No Metadata Yet" };

        var entry = ItemMapper.ToEntry(movie);

        var ids = entry["ids"];
        Assert.NotNull(ids);
        Assert.IsType<JsonObject>(ids);
        Assert.Empty((JsonObject)ids!);
    }

    [Fact]
    public void ToEntry_with_year_emits_year()
    {
        var movie = new Movie { Name = "Dune", ProductionYear = 2024 };

        var entry = ItemMapper.ToEntry(movie);

        Assert.Equal(2024, (int?)entry["year"]);
    }

    [Fact]
    public void ToEntry_without_year_omits_year_key()
    {
        var movie = new Movie { Name = "Dune" };

        var entry = ItemMapper.ToEntry(movie);

        Assert.False(entry.ContainsKey("year"));
    }

    [Fact]
    public void ToEntry_drops_blank_provider_id_values()
    {
        var movie = new Movie { Name = "X", ProviderIds = { ["Tmdb"] = "" } };

        var entry = ItemMapper.ToEntry(movie);

        Assert.Null(entry["ids"]!["tmdb"]);
        Assert.Empty((JsonObject)entry["ids"]!);
    }

    [Fact]
    public void ResourceFor_movie_returns_movies()
    {
        Assert.Equal("movies", ItemMapper.ResourceFor(new Movie()));
    }

    [Fact]
    public void ResourceFor_series_returns_shows()
    {
        Assert.Equal("shows", ItemMapper.ResourceFor(new Series()));
    }

    [Fact]
    public void ResourceFor_episode_returns_null()
    {
        // Episodes are covered by their parent series.
        Assert.Null(ItemMapper.ResourceFor(new Episode()));
    }

    [Fact]
    public void ResourceFor_audio_returns_null()
    {
        // Out of scope; non-movie/show types fall through the switch.
        Assert.Null(ItemMapper.ResourceFor(new MusicAlbum()));
    }

    [Fact]
    public void HasProviderIds_returns_false_for_empty_dict()
    {
        Assert.False(ItemMapper.HasProviderIds(new Movie()));
    }

    [Fact]
    public void HasProviderIds_returns_true_when_any_key_set()
    {
        var movie = new Movie { ProviderIds = { ["Tmdb"] = "1" } };
        Assert.True(ItemMapper.HasProviderIds(movie));
    }
}
