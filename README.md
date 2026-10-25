# StreamFin IPTV

Xtream Codes and M3U for Jellyfin, installed the same way as other plugins. Live TV gets a guide. Films and shows become real Jellyfin libraries, and they play through StreamFin instead of failing the way the channel-style Xtream plugins do.

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

## Add your provider

1. Open **Dashboard → StreamFin IPTV**.
2. Paste the playlist or player link your provider sent. The server, username, and password are filled in from it. You can also type those three fields yourself.
3. Leave **Live TV**, **Films**, and **Shows** checked.
4. Choose **Save and start**.

StreamFin creates the Live TV tuner, the guide, and the movie and series libraries, then starts the first sync. Nothing else has to be configured in Jellyfin's library or Live TV screens.

## What you get

- Xtream Codes and M3U
- Live TV with the provider's XMLTV guide
- Films and shows as normal libraries: seasons, episodes, resume, Next Up
- Playback that sends a player User-Agent and tries `mp4`, `mkv`, and `ts` when the panel advertises the wrong file type

Library files are written inside Jellyfin's data folder. Leave **Generated library folder** blank unless you want them somewhere else.

## License

MIT. See [LICENSE](LICENSE).
