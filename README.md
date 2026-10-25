# StreamFin IPTV

A Jellyfin plugin that treats an Xtream Codes panel or an M3U playlist like a regular IPTV player: live TV with a guide, plus **movies and series that actually play**.

Other available Jellyfin IPTV plugins typically expose films and shows as a channel browse surface, or write a raw panel URL into a `.strm` file and let ffmpeg open it. Both fail in the same way: the panel wanted a browser or VLC User-Agent, a Referer, or a different container (`mp4` vs `mkv` vs `ts`), and ffprobe comes back with "streams and format are both null". StreamFin plays the title itself, then hands the bytes to Jellyfin.

## What it does

- **Xtream Codes** (`player_api.php`) and **M3U / M3U-plus** playlists
- Live TV tuner with XMLTV guide matching
- Real **Movies** and **TV Shows** libraries (seasons, episodes, resume, Next Up)
- Playback proxy with User-Agent, Referer, redirect following, and container fallbacks
- Category filters, title excludes, and per-category limits
- Automatic library, tuner, and guide setup
- Tolerant JSON and M3U parsers that survive the replies resellers actually send

## Install

1. Build the plugin (see below) or grab a release zip.
2. Copy `Jellyfin.Plugin.StreamFin.dll` into Jellyfin's `plugins/StreamFin` folder.
3. Restart Jellyfin.
4. Open **Dashboard → StreamFin IPTV**.
5. Add your subscription, test it, save, then **Set up libraries and Live TV** and **Sync now**.

Use the `net9.0` build on Jellyfin 10.11, and the `net10.0` build on Jellyfin 12.

### Docker

Mount the plugin directory into the container, for example:

```text
/config/plugins/StreamFin/Jellyfin.Plugin.StreamFin.dll
```

The generated library files default to Jellyfin's data directory (`streamfin/media`). Give the container write access to that path, or set **Generated library folder** to a folder you already mount.

## Build

```bash
dotnet build Jellyfin.Plugin.StreamFin.sln -c Release
```

The assemblies land in:

```text
src/Jellyfin.Plugin.StreamFin/bin/Release/net9.0/Jellyfin.Plugin.StreamFin.dll
src/Jellyfin.Plugin.StreamFin/bin/Release/net10.0/Jellyfin.Plugin.StreamFin.dll
```

Package a zip for Jellyfin 10.11:

```bash
dotnet build src/Jellyfin.Plugin.StreamFin/Jellyfin.Plugin.StreamFin.csproj -c Release -f net9.0
mkdir -p artifacts/StreamFin
cp src/Jellyfin.Plugin.StreamFin/bin/Release/net9.0/Jellyfin.Plugin.StreamFin.dll artifacts/StreamFin/
cp src/Jellyfin.Plugin.StreamFin/meta.json artifacts/StreamFin/
```

## Settings that matter for playback

- **Play films and shows through StreamFin** (on by default). This is the player path. The generated `.strm` files point at StreamFin; StreamFin talks to the panel.
- **User agent** — leave blank unless your provider told you to send a specific one.
- **Referer** — leave blank to use the panel address. Set it if the provider requires a specific value.
- **Live stream format** — MPEG-TS starts faster. Switch to HLS only if the provider serves it better.

## Why movies and series load here

1. Catalog comes from `get_vod_streams` / `get_series` / `get_series_info` (or from the M3U), not from a flat channel list.
2. Titles are cleaned (`EN - Show S01E01 [1080p]` becomes *Show*, season 1, episode 1) and written as a real library of `.strm` + `.nfo` files.
3. Playback does not trust the advertised `container_extension`. It tries `mp4`, `mkv`, `avi`, `ts`, and friends until one answers.
4. Requests go out with a browser User-Agent and a Referer, then follow the panel's redirects.
5. HLS playlists are rewritten so segments also go through StreamFin.

## Repository

<https://github.com/jackalgg/Jellyfin-Xtream-IPTV-player>

## License

MIT. See [LICENSE](LICENSE).
