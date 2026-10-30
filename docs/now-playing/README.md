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
- `cover` is there only with **Send the song's cover picture too** ticked and when the file has one: the picture as a
  `data:image/jpeg;base64,…` (or `image/png`) address, at most 1 MB. The PHP example saves it as `cover.jpg` (or
  `cover.png`) and puts that file's address in `nowplaying.json` instead.
- Songs marked **Not on the website** in the library (right-click → *Not on the website*) are never sent, nor shown
  as `next`: the website keeps what it had. RDS is not affected.
- **Hold every change back by** delays every post and file by that many seconds: set it to the stream's delay, so
  the website changes when listeners hear the song.

- `now` and `next` are `null` when there is nothing (the player is stopped, or the playlist ends).
- `type` is `music`, `jingle`, `advert`, `stream` or `programme` (news and shows from the schedule). With **Songs
  only** ticked (the default), nothing is sent during jingles, adverts and programmes, so the website keeps showing
  the last song.
- `duration` is in seconds (0 for a stream that plays until stopped). Times are UTC.
- The key you set is sent in the `X-Api-Key` header. A page should refuse posts without it.
- Any answer from 200 to 299 counts as received. Otherwise the app tries again every 30 seconds, and only sends the
  latest information.

## Files and FTP

Under **Files and FTP**, each output is written whenever now playing changes (after the delay, with the same rules:
songs only, nothing marked *Not on the website*):

- **A text** from a template: one line (`{artist} - {title}`) for the website or a streaming encoder that reads a
  file, or a whole XML or HTML page, on several lines. The fields are `{artist}`, `{title}`, `{type}`, `{duration}`
  (m:ss), `{started}` (local hh:mm), `{next_artist}`, `{next_title}`, `{station}`, `{show}` and `{presenter}`. With
  nothing on air, `{title}` is the station's name. In a file ending in `.xml`, `.html`/`.htm` or `.json` the fields
  are escaped for it (`&` becomes `&amp;`…); in other files a line loses the separators an empty field leaves
  (`{artist} - {title}` with no artist gives just the title). Text is UTF-8.
- **The cover picture** of the song, as it is in the file (JPEG or PNG). For songs without one it writes the picture
  you choose (the station's logo), or leaves the file as it was.

Where it goes:

| Address | How |
|---|---|
| `C:\web\nowplaying.txt`, `/srv/www/np.xml`, a share | written whole (to a temporary file, then renamed) |
| `ftp://ftp.example.com/www/nowplaying.txt` | FTP upload (passive), with the user and password; `ftps://` for FTP over TLS |
| `https://www.example.com/nowplaying/nowplaying.php?file=nowplaying.txt` | HTTP `PUT` of the file, with the password box sent as the key in `X-Api-Key` |

Any answer from 200 to 299 counts as received. An output that fails is tried again every 30 seconds with the latest
information; the others go on.

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
| `cover.jpg` / `cover.png` | The song's cover, when the app sends covers |

It also keeps the files of the outputs that are sent to it: an output's address is the page with `?file=` and a
name (`nowplaying.php?file=cover.jpg`), and its password box has the same key. Only plain names ending in `.txt`,
`.xml`, `.html`, `.htm`, `.json`, `.jpg`, `.jpeg` or `.png` are taken (never `.php`, nor its own `nowplaying.json` and
`history.json`), up to 2 MB.

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
