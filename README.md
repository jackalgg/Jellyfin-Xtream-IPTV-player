# StreamFin IPTV

Xtream Codes and M3U for Jellyfin. Paste a provider link and watch live TV, films, and shows immediately. Nothing is copied into libraries unless you ask for that.

## Install

1. Open the admin dashboard and go to **Plugins**.
2. Open the **Repositories** tab.
3. Click **+**.
4. Name: `StreamFin`
5. Repository URL:

   ```text
   https://raw.githubusercontent.com/jackalgg/Jellyfin-Xtream-IPTV-player/main/repository.json
   ```

6. Save.
7. Open the **Catalog** tab.
8. Under **Live TV**, choose **StreamFin IPTV**.
9. Click **Install**.
10. Restart Jellyfin.

Use the **10.11** build on Jellyfin 10.11, and the **12** build on Jellyfin 12. The catalog offers the one that matches the server.

## Watch

1. Open **Dashboard → StreamFin IPTV**.
2. Paste the playlist or player link your provider sent. The server, username, and password are filled in from it. You can also type those three fields yourself.
3. Choose **Save**.
4. Open **Watch**.

Categories load from the provider. Opening one fetches only that category, then a title plays when you click it. An M3U playlist is downloaded once; after that, groups open immediately.

Live channels try HLS first and fall back to MPEG-TS. Films and episodes play in the browser when the panel serves a format the browser understands. Playback sends a player User-Agent and tries `mp4`, `mkv`, and `ts` when the panel advertises the wrong file type.

## Library copy

Leave **Copy the catalog into Jellyfin libraries** off. That copy writes every film and episode into Jellyfin and is what makes setup stall. Turn it on only if you want those libraries, resume, and Next Up, then choose **Sync now**.

Library files are written inside Jellyfin's data folder. Leave **Generated library folder** blank unless you want them somewhere else.

## License

MIT. See [LICENSE](LICENSE).
