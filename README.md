# StreamFin IPTV

StreamFin plays Xtream Codes and M3U in Jellyfin the way an IPTV player does. Paste the link from your provider, open a category, and watch live TV, films, and series.

Titles stream from the provider. Copying the catalog into Jellyfin libraries is optional and off unless you turn it on.

## Requirements

- Jellyfin 10.11 or Jellyfin 12
- An Xtream Codes subscription or an M3U playlist

The catalog installs the build that matches the server.

## Install

1. Open the admin dashboard and go to **Plugins**.
2. Open the **Repositories** tab and click **+**.
3. Name: `StreamFin`
4. Repository URL:

   ```text
   https://raw.githubusercontent.com/jackalgg/Jellyfin-Xtream-IPTV-player/main/repository.json
   ```

5. Save.
6. Open the **Catalog** tab.
7. Under **Live TV**, choose **StreamFin IPTV** and click **Install**.
8. Restart Jellyfin.

## Watch

1. Open **Dashboard → StreamFin IPTV**.
2. Paste the link your provider sent. These all work:
   - `http://example.com:8080/get.php?username=...&password=...`
   - `http://example.com:8080/player_api.php?username=...&password=...`
   - an M3U playlist URL
3. The server, username, and password fill in from an Xtream link. You can type those three fields yourself instead.
4. Choose **Save**.
5. Choose **Watch**.

Open a category, then click a title.

- A live channel starts playing.
- A film starts playing.
- A series opens its episodes. Click an episode to play it.

Search and **More** stay inside the category you opened. StreamFin asks the provider for that category when you open it, so a large subscription does not have to download every title first. An M3U playlist is downloaded once; after that, its groups open immediately.

## Libraries

Leave **Copy the catalog into Jellyfin libraries** unchecked. Watch already plays the provider.

Turn that option on only when you want the titles inside Jellyfin's own movie and series libraries, with resume and Next Up. Then choose **Sync now**. On a large provider that copy takes a while, because every film and episode is written out.

Generated files stay in Jellyfin's data folder. Set **Generated library folder** only when you want them somewhere else.

## Update

Open **Dashboard → Plugins**, update **StreamFin IPTV**, and restart Jellyfin.

## License

MIT. See [LICENSE](LICENSE).
