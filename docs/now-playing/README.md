# Now playing on the station's website

Sintoniza can send what is on air to your website (Settings → **Website**). Whenever the item on air (or the next
one) changes, the app sends a `POST` to the address you set, with this JSON body:

```json
{
  "station": "Rádio Exemplo",
  "updated": "2026-10-15T10:31:07Z",
  "now":  { "artist": "Dulce Pontes", "title": "Canção do Mar", "type": "music", "duration": 212.4, "started": "2026-10-15T10:31:06Z" },
  "next": { "artist": "Xutos & Pontapés", "title": "A Minha Casinha", "type": "music", "duration": 185, "started": null },
  "show": { "name": "Manhãs", "presenter": "Rita", "ends": "2026-10-15T11:00:00Z" }
}
```

- `show` is the live show on air (Sintoniza Pro → Shows), with when it ends; `null` when none is.

- `now` and `next` are `null` when there is nothing (the player is stopped, or the playlist ends).
- `type` is `music`, `jingle`, `advert`, `stream` or `programme` (news and shows from the schedule). With **Songs
  only** ticked (the default), nothing is sent during jingles, adverts and programmes, so the website keeps showing
  the last song.
- `duration` is in seconds (0 for a stream that plays until stopped). Times are UTC.
- The key you set is sent in the `X-Api-Key` header. A page should refuse posts without it.
- Any answer from 200 to 299 counts as received. Otherwise the app tries again every 30 seconds, and only sends the
  latest information.

## The PHP example

[`nowplaying.php`](nowplaying.php) is a ready-to-use receiver for any web host with PHP 7.4 or newer:

1. Change `API_KEY` at the top of the file.
2. Upload it to a folder of your website that PHP can write to, e.g. `/nowplaying/`.
3. In the app, set the address (`https://www.example.com/nowplaying/nowplaying.php`) and the same key, then click
   **Send now**. The answer should be "Sent".

It keeps three files next to itself:

| File | What it has |
|---|---|
| `nowplaying.txt` | One line, `Artist - Title` (the station name when nothing is on air) |
| `nowplaying.json` | Everything the app sent |
| `history.json` | The last 20 songs, newest first |

Opened with a `GET` (a browser, or your website's JavaScript), the same address answers with `nowplaying.json`.
For example:

```html
<p id="now-playing"></p>
<script>
  async function nowPlaying() {
    const info = await (await fetch('/nowplaying/nowplaying.php')).json();
    const now = info.now;
    document.getElementById('now-playing').textContent =
      now ? (now.artist ? `${now.artist} - ${now.title}` : now.title) : (info.station ?? '');
  }
  nowPlaying();
  setInterval(nowPlaying, 15000);
</script>
```
