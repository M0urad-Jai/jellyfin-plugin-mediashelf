# MediaShelf Sync — Jellyfin Plugin

Pushes your Jellyfin activity to a [MediaShelf](https://github.com/M0urad-Jai/mediashelf-app)
instance via its `/api/v1` API. MediaShelf is a self-hosted library tracker
for books, movies, albums, shows, anime, and manga — this plugin keeps the
"movies" and "shows" sides of your MediaShelf in sync with what you watch
in Jellyfin.

- **New library items** (movies, series) → `POST /api/v1/sync/collection`
- **Watched movies/episodes** → `POST /api/v1/sync/history` (episodes include season/episode numbers against the parent series)
- **Ratings** → `POST /api/v1/sync/ratings` (Jellyfin's 1–10 scale is converted to MediaShelf's 0–5)
- **In-progress movie playback** → `POST /api/v1/sync/playback` (throttled, default every 30s)

Per-episode playback percentage isn't synced — MediaShelf tracks progress at the show level, not per episode, so only the "episode watched" event is sent.

## Build

Requires the .NET 9 SDK. The `.csproj` is pinned to `Jellyfin.Controller` `10.11.*`, matching Jellyfin 10.11.x (including 10.11.11).

```bash
cd jellyfin-plugin-mediashelf
dotnet build -c Release
```

If your server is on a different major.minor (10.10.x, 10.9.x, etc.), edit `JellyfinControllerVersion` in the `.csproj` and `targetAbi` in `build.yaml` to match before building — the package version must line up with your server's ABI or the plugin won't load.

## Install

1. Create a folder in your Jellyfin server's plugin directory: `<jellyfin-data>/plugins/MediaShelf Sync/`
2. Copy into it:
   - `bin/Release/net9.0/Jellyfin.Plugin.MediaShelf.dll`
   - `meta.json` (from this repo — do **not** skip this; without it Jellyfin has to guess the plugin's identity from the folder name, which is a common source of it silently not registering)
3. Restart Jellyfin.
4. Dashboard → **Plugins → My Plugins** — confirm "MediaShelf Sync" shows status **Active**. If it shows "Not Supported" or is missing entirely, check the server log for a line like `Loaded plugin: "MediaShelf Sync"` — if it's absent, the DLL failed to load (see Troubleshooting below).
5. Open **Plugins → MediaShelf Sync**, enter your MediaShelf URL and the API token from MediaShelf's **Settings → API** page, choose what to sync, and **Save**.
6. Reload the page and confirm the fields you just entered are still populated. If they revert to blank, see Troubleshooting.

## Troubleshooting

**Config page loads but fields don't save/persist:**
- Open your browser's DevTools (F12) → Network tab, click Save, and check the `POST .../Plugins/b6e2c8a4-.../Configuration` request. A non-200 response or a request that never fires (JS error in the Console tab instead) tells you which side is broken.
- If you're behind Cloudflare (or another proxy) with a minification/JS-optimization feature like Rocket Loader enabled, disable it for your Jellyfin domain — it's known to break Jellyfin's plugin settings pages by deferring inline `<script>` execution.
- Confirm you're not hitting a cached copy of an old `configPage.html` — hard-refresh (Ctrl/Cmd+Shift+R).

**Plugin loads but nothing syncs:**
- Check the server log for `MediaShelf:` entries — every failure (bad URL, network error, non-2xx from MediaShelf) is logged there, including the response body.
- Confirm the URL you entered doesn't have a trailing slash and includes the scheme (`https://...`).
- Confirm the API token hasn't been revoked in MediaShelf's Settings → API.
- Trigger a test event: add a movie to a library Jellyfin is watching, or mark one as watched, and check the log immediately after.

## Notes

- One token = one MediaShelf account. Everything the server sees is written to that account; this plugin doesn't map individual Jellyfin users to individual MediaShelf accounts.
- Matching relies on provider ids (TMDB/IMDb/TVDB/AniList/MyAnimeList) when Jellyfin has them; otherwise MediaShelf falls back to title + year.
- If MediaShelf is unreachable, failures are logged to the Jellyfin server log and otherwise ignored — playback in Jellyfin is never affected.
