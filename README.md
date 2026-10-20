# Sintoniza Radio Broadcast system

A broadcast radio automation / playout app for **Linux, macOS and Windows**, with the same look on all three.
It is built in C# with [Avalonia](https://avaloniaui.net/) and uses the [BASS](https://www.un4seen.com/) audio
engine for decoding, mixing and streaming.

![Main window](docs/screenshot.png)

> **Status:** in development. It plays a playlist on air with crossfades, fires jingles from a cartwall, streams to
> Icecast/Shoutcast, plays events and ad breaks at set times, levels loudness, sends now playing to an RDS encoder
> and the station's website, keeps a music library, play history and as-run log (music reports and proof of
> broadcast), and guards against dead air. Hour templates and rotation are next (see [Roadmap](#roadmap)).

## Features

- **On-air deck**: title and artist, the album cover (when the file has one), a scrolling waveform with a playhead, a full-track overview with the
  crossfade zone marked, elapsed/remaining time (turns red in the last 10 s), and the next item.
- **Transport**: Play, Pause, Next (crossfades straight away) and Stop. The button for the current state lights up,
  and Next flashes on every track change. **Adverts are protected**: while one is on air, Next, Pause and Stop
  (and putting another item on air) ask first, and only a click on the button in that question does it, so a paid
  spot isn't cut by accident but still can be in an emergency. The mode button switches between **AUTO**
  (runs through the playlist) and **ASSISTED** (stops after each item until you start the next).
- **Playlist**: drag and drop from the built-in music browser or your file manager, drag rows to reorder,
  estimated start time for each item, played/on-air/next markers, a type icon (music, jingle, commercial, stream,
  programme), a tag on items put there by a scheduled event or ad break, and an on-air row that fills from left
  to right as the track plays. Everything that isn't a song has its own background: ad breaks green, jingles and
  promos purple, programmes and news blue, streams teal; each can be changed in Settings → Playlist. Played items
  show when they went on air (kept across a restart), and a line marks where each hour starts, before and after
  the item on air. **This hour** (on by default) shows the hour before, this hour and the first 30 minutes of the
  next (Settings → Playlist), and the item on air; off, everything still to play. Either way, what played before
  the hour before isn't shown (Reports has it). The item on air is kept at the top of the list; scrolling away
  stops that until the next item goes on air, and **On air** (or **This hour**) jumps straight back to it. M3U
  open/save.
- **Internet streams in the playlist**: put another station's stream in the playlist (**Stream…**) for a set time
  (it crossfades into the next item when the time is up) or until you press Stop or Next. It connects a few seconds
  before it is due, so there is no dead air, and if the stream drops the playlist moves on. Saved with the playlist
  and in M3U files (`#EXTINF:-1` means until Stop).
- **Music library**: a database of the station's music, an SQLite file or a shared MySQL / MariaDB server
  (Settings → Library). Beside the playlist, tabs show the **CARTWALL**, the **LIBRARY** and the **FOLDERS**. The
  library tab (Ctrl/Cmd+F) searches every song's artist, title, album and file name as you type, ignoring case and
  accents, in a table (title, artist, year, category, length), and filters by category: Current, Recurrent, Gold,
  Jingles, IDs, Promos, Ads, or your own (the ✎ button next to the filter, or Settings → Library → Categories,
  adds, renames, recolours, orders and deletes categories). Double-click or Enter adds a song to the playlist and
  shows it there. **Categories make a tree** (*Music › 80s › Rock*): choosing a category anywhere takes in the ones
  under it. **A song can be in several categories**: its own, and others it is also in (right-click → Also in).
  **Each category can have folders**: their songs (subfolders too) go in it, and so do new songs put there later;
  a folder added there is scanned by the library by itself.
  Right-click sets the category, tempo, voice and language of one or many songs. On the **Folders** tab, right-click
  a folder → Category puts all its songs (subfolders too) in a category, and new songs found there by a scan get it
  as well. The folders of categories (from Sintoniza Import or the categories editor) are scanned and watched too,
  on every studio sharing the library. Songs whose files are deleted, and all the songs of a deleted folder, are
  taken out of the library by the next scan (a drive or share that isn't connected is left alone); fill music and
  the daily playlists pass over a file that isn't there. **Scan** adds new music and
  follows moved or renamed files, keeping their category, cue points and history. The app also scans by itself:
  a minute after it starts, every hour, and shortly after files in the music folders change (all adjustable in
  Settings → Library). Every item that goes on air is written to the play history. Studios sharing a MySQL
  library share categories, cue points and history. `radio-import` does the scanning from the command line. The
  tables are documented for other programs in [docs/library-database.md](docs/library-database.md).
- **Sintoniza Import** — a separate program (its own entry in the app menu / Start menu, or
  `radio-playout --import`) that brings a folder of audio into the library in categories. Choose the folder, the
  category (new or existing, at the top or under another one), and whether the folders inside it become
  sub-categories, 1 to 3 levels deep (`Music/80s/Rock` → *Music › 80s › Rock*). **Preview import** shows the
  categories it will make, which are already there, and how many files go in each, how many are in the library
  already and how many are in another category, before anything is changed. Songs already in another category
  keep it and are also in the new one, move to it, or are left alone (your choice). **Import** makes the
  categories, reads the songs into the library, puts each in its category, and keeps every folder for its
  category so songs put there later get it too. It can be stopped and run again.
- **Daily playlists** — the **Daily playlists** tab of Sintoniza Pro (or `sintoniza-pro --playlists` to
  open on it): place hour templates for your shows on the calendar (Playlists tab), then build each hour song by
  song or let your rules fill it. Each hour is made ahead of time from the hour template placed there: its lines in
  turn, until the hour is full with the ad breaks and events booked in it, keeping songs and artists apart (the rest
  times of fill music, counting what played and what is planned) and never the same artist twice in a row while
  another can play; songs whose file is gone are passed over. Today and the next 2 days are always made (Settings →
  Playlist), and an hour whose template changed is made again within minutes, unless it was changed by hand or is
  about to play. The tab shows a day's 24 hours and, for each, every song and jingle in order with its start
  time and where it came from, and the ad breaks (each advert with its client, as booked now) and events in their
  place, with the hour's total. **Another song** replaces a song with another from the same template line;
  ▲ ▼ and Remove change the order; double-click a song in the library beside it to put it in. **Listen** (▶, a
  double-click, or Space) plays the selected item or a library song (right-click → Listen) on the **Preview** output
  only (Settings → Audio): the manager never opens the on-air cards, and warns if Preview is set to the on-air one.
  **Edit tags…** (or right-click a library song) writes title, artist, album… in the file; the library and the planned
  hours follow, and a song that is on air right now is refused until it has finished. An hour changed by
  hand (✎) is never made again by itself; **Make this hour again** / **Make the day again** do it on purpose.
  The last item or two of each hour are **optional**. **On air**, the playout plays only these playlists: it puts
  each hour in the playlist about half an hour before it plays (an empty playlist in AUTO gets the hour from the
  item planned for now), and near the end of each hour it keeps time: when the next hour would start late it
  drops an optional item, when it would start early it adds a song, so each hour starts on time. The host can
  still change the playlist on air (adverts stay protected); the plan stays as it was and the as-run log has what
  really played. With no template in an hour, or the database out of reach without a copy, fill music plays (the
  playout keeps a copy of the next day of plans for when the database can't be reached).
- **Everything in the database**: the schedule (events, ad breaks, hour templates, clients and campaigns), the daily playlists, the
  folders of each category and the as-run log are in the library database, with the music. With MySQL, Sintoniza
  Pro can run on another computer (the commercial department) and the studio picks up a save within seconds.
  If the database can't be reached, the playout carries on with its copy of the last schedule and its own as-run
  files, and puts in what the database missed when it is back. The first start with this version moves an older
  `schedule.json` into the database. The tables are in [docs/library-database.md](docs/library-database.md).
- **Folders**: folder tree plus a filterable file list. Double-click to add, or right-click → *Play next*.
- **Cue points**: mark the usual radio markers on any file: **Cue In** (start), **Intro** (talk-over until the
  vocals), **Outro** (start of the ending), **Mix** (where the next item starts while this one finishes) and
  **Cue Out** (stop). Playback, lengths, start times and the waveforms follow them, and an INTRO / OUTRO countdown
  appears above the waveform for talking over. See [Cue points](#cue-points).
- **Tag editor**: right-click a playlist item or a file in the music browser → *Edit tags…* (Ctrl/Cmd+T) to
  change title, artist, album, genre, year, track number and comment, and to set or remove the cover picture
  (choose a JPEG/PNG or drop one on it). MP3s get ID3v2.3 tags; FLAC, OGG, M4A and WAV work too. The file on air
  can't be edited until it has finished playing.
- **Cartwall**: 12 instant buttons that play over the music, with their length, and a countdown and a progress fill
  while playing. The station's cartwalls are made in Sintoniza Pro; the host picks one and fires its carts.
- **Station panel**: logo, station name, clock and date, countdown to the top of the hour, playlist end time,
  tracks played today, stream time (when streaming is used), the next scheduled event, and stereo peak meters.
- **Pro** — a separate program, **Sintoniza Pro** (the station's production program; its own executable,
  `SintonizaPro` / `sintoniza-pro`, with its own entry in the app menu / Start menu and on the desktop):
  the **station's settings** (name, logo, crossfades, fill music, time signal, streaming, RDS texts, website, alerts),
  the **categories** (under eight fixed ones: Music, Jingles, Promos, ProgAutor, Fillers, Redação, Spots, Takes), the
  **cartwalls**, music import, and events, ad breaks, clients, hour templates and the daily playlists, for whoever
  runs the station; the on-air screen has no way into it. The on-air program's own Settings only has what belongs to
  that computer (sound cards, microphone, the database, keys). Save writes `schedule.json` and the playout picks it up within a few seconds (it keeps when each event
  last ran in `schedule-state.json`, so the two never write the same file). It places what happens at set times on
  **week calendars** (days across, times down, like a traffic system). Click a cell to put the selected item there, drag
  to fill many cells, or click a time for every day; rows can be every 5 to 60 minutes from any minute (every hour
  at :20), and exact times such as 12:59:55 can be added as rows. Other items show faintly in their cells.
  - **Events** play a file, the newest file in a folder (a news bulletin downloaded there; optionally only if it
    is recent) or another station's stream for a set time.
  - **Ad breaks** have a **running order**, built like a small playlist: jingles and named **advert sections**
    (for example *Antes do SH*, *Depois do SH*, *Promos*). A section plays the adverts booked into it that day; a
    jingle can be set to play only when the section after it has adverts (a section opener). A break with no
    adverts at all is skipped.
  - **Clients**: each client has campaigns. A campaign has its dates, one or more audio files
    (they take turns), the **section** it goes in and its **position**: automatic, 1–5 from the start of the
    section or 6–10 at its end (10 = last); automatic ones never put the same client twice in a row where that can
    be helped. The campaigns calendar shows a real week (with week navigation): one row per break time, and in
    each cell how many adverts the campaign's section has there and how long they run (`02 | 0:45`). A click places
    the selected campaign in that break every week, or on that date only; only breaks that have its section can be
    clicked. The client's name goes on the proof of broadcast. Adverts made with earlier versions become a client
    and a campaign each.
  - **Shown ahead in the playlist**, item by item (a break's jingles and each advert with its advertiser, each with
    its start time): the events and breaks coming up appear in the playlist where they will play
    (tagged SCHEDULED, with their start time and how they start: hard, can wait N minutes, or ends at its time),
    and the start times after them allow for them. They become real playlist items when they are due.
  - **Playlists** (hour templates): templates whose lines play in order — a song (or
    jingle, advert…) from a music library category, or a file from a folder — placed on the week calendar by hour.
    The daily playlists are made from them ahead of time (see **Daily playlists**).
  - **Missed while stopped**: scheduled items whose time passed more than 2 minutes ago while the player wasn't
    playing (stopped overnight, after a restart) are left out, with a line in the log, not played late one after
    the other.
  - **Ad breaks stand out**: a green band behind their rows, the money sign (Settings → Station; empty = this
    computer's currency) instead of an icon on each advert, and the advertiser under it. When the next break is
    less than 10 minutes away, a **BREAK IN mm:ss** countdown shows on the deck with how it starts, red in its
    last minute.
  - Each starts in one of three ways:
    - **soft**: after the item on air ends, optionally at most N minutes late (then what is on air fades out). A
      window such as "between :48 and :55" is a soft break at :48, at most 7 minutes late;
    - **hard**: on the second, fading out what is on air;
    - **ends at its time** (backtimed): for a break that has to finish before the news, e.g. at 59:55. It goes into
      the playlist 15 minutes before, where it will fit best, and the songs before it are adjusted as the hour goes
      on: a song that wouldn't fit moves after the break, a gap is filled with a fill song that fits
      (Settings → Safety), and whatever is still on air when the break has to start fades out. Gaps under 15 s
      are left, so the break starts that much early.
  - Its items go into the playlist tagged with its name, and the playlist carries on after them. With the player
    stopped they wait as next. An event that fell due while the app was not running (a crash, a restart) still
    starts if it is at most 2 minutes late; older ones are logged as missed.
  - **What the host may change**: each event and break sets whether its items can be removed and moved, moved
    only, or neither (locked, the default for ad breaks). Locked items show a padlock; Remove, drag and Clear leave
    them, and starting a later item by hand keeps them in the playlist after it. A host can still press Next or
    Stop in an emergency; that is noted in the log, and the as-run log shows how long the item played.
- **As-run log and reports** (the *Reports* button): everything that goes on air, from the playlist, scheduled
  events, ad breaks and carts, is logged with its start and end time, one CSV file a day in the `as-run` folder
  next to the settings. The Reports window makes, for any days:
  - **Music played** and **music totals per song** for the collecting societies (royalties), with album, composer,
    label and ISRC from the files' tags;
  - **Adverts (proof of broadcast)**, for all advertisers or one;
  - the whole log.
  Save them as CSV (comma or semicolon separated) or as a page to print or save as PDF.
- **Loudness levelling** (Settings → Audio): every item plays at the same perceived loudness. Each file is measured
  (EBU R128) in the background when it is added to the playlist, a cart or the schedule, and remembered; loud
  songs are turned down and quiet ones up (at most 6 dB by default, never past -1 dBFS peaks). The files are not
  changed. Off by default: switching it on makes the output quieter than most modern masters, so check the desk.
- **Now playing on the website** (Settings → Website): when what is on air changes, the app posts it as JSON to an
  address you choose, with a key. [docs/now-playing](docs/now-playing) has the format and a PHP page that keeps it
  in `nowplaying.txt` and `nowplaying.json` for the website to show.
- **Streaming**: MP3 or OGG to Icecast 2 or Shoutcast v1, with "now playing" titles sent to the server.
  Click the STREAM button to start and stop (also while it is still connecting), or right-click it for Start / Stop;
  after an error a click retries and right-click → *Turn streaming off* clears it. It can also have a shortcut.
  One click on the **STREAM** badge next to ON AIR starts or stops it. The badge and the stream time show on a web
  radio, or on an FM station once a streaming server is set up (a source password is entered); otherwise they are
  hidden.
- **Separate outputs for the mixing desk**: music and cartwall can each go to their own sound card or output pair
  (Out 1–2, 3–4, …), so they come up on separate desk faders. A test tone helps you find which fader is which.
  The stream always carries music and cartwall together. A third output, **Preview**, is for pre-listening in the
  cue editor and is never streamed.
- **Web radio microphone**: set the station type to *Web radio* (Settings → Station) and a **MIC** button appears.
  Hold it (or F9) to talk, or choose on the Microphone tab to press it once to open the mic and again to close it.
  Music and cartwall fade down to a set level while it is open and come back up when it closes. The mic
  goes into the stream only, not to the audio outputs, so you don't hear yourself with a delay; use headphones.
  The input, mic level, music level while talking and fade times are on the Microphone tab. With *FM radio* the
  mic goes through the mixing desk and there is no MIC button.
- **Backups** (Settings → Safety): every day at a set time (and *Back up now*), one zip with the database (MySQL:
  an SQL file that puts every table back with `mysql name < file`, made by the app, so it works on Windows too;
  SQLite: a copy) and the station's files; kept for 14 days (the newest three always).
- **Alerts** (Settings → Alerts): by Telegram, email and/or a web address (JSON POST), when there is silence on air,
  the stream is down, the engine stops responding, the app had to be started again after a crash or a freeze, the
  database is unavailable, a disk is nearly full, the computer's clock is off (checked against a time server) or a
  backup fails; and again when it is fixed. *Send a test alert* checks the setup.
- **Graphics on Linux with NVIDIA**: the app tells the driver to sleep instead of spinning a CPU core while it waits
  for the screen (`__GL_YIELD=USLEEP`, unless set already).
- **Top of the hour time signal** (Settings → Time signal): the pips (five short, the long one on the hour) or your own
  audio file, timed to end on the hour. Either play it **over** whatever is on air, or **stop** what is on air: it
  fades out just before the signal and the next playlist item starts right after it (in AUTO). Or turn it off.
  It plays on the Cartwall output and in the stream, and follows the computer's clock. **Hour announcements**: a
  folder with a file for each hour, the hour's number at the end of the name (`HRS05.mp3` at 05:00, `HRS22.mp3`
  at 22:00; midnight 00 or 24), played instead of the signal (ending on the hour) or right after it; an hour
  without a file gets the signal only.
- **Languages**: English, Português and Français (Settings → Station → Language, from the next start; automatic
  follows the computer). Every window and message is translated; dates and day names follow the language. The
  translations are in `src/Playout.App/i18n/`; `python3 scripts/i18n-check.py` lists texts that have none.
- **Crossfade**: song to song as set in Settings → Audio; into or out of a jingle, promo, advert or programme its
  own value, 0 by default (the song ends, the jingle starts clean and plays to its end).
- **Settings** (Ctrl/Cmd+,): station name, logo, genre, website and station type, crossfade length, audio outputs,
  loudness levelling, microphone, time signal, safety, library, keyboard shortcuts, the streaming server, RDS and
  the website.
- **Updates**: checks GitHub Releases at start and every 6 hours (can be turned off in Settings → About, which also
  has *Check now*). A new version shows a green *Update available* button; nothing is installed until you click
  it. Installed with the Windows setup or the .deb or .rpm package, it downloads the package, checks its SHA-256 and
  installs it (Windows: the app closes and restarts on the new version; Linux: asks for the admin password, then
  offers a restart). Portable zip/tar copies open the download page instead.
- **RDS** (Settings → RDS): the radio text shows artist and title of the song on air (your own template, up to 64
  characters) and the station's text during jingles, adverts and when stopped; the PS is fixed. It is sent when
  the item changes and again every minute, so a restarted encoder catches up. Encoders are reached by TCP, UDP or
  a serial port, with **ASCII commands** (`RT=` / `PS=` by default, editable: PIRA32, P232, MRDS, DEVA and most
  others) or **UECP**; or the text is written to a file. *Send now* tests the connection. With ASCII commands it
  can also send **RT+** tags (where the title and the artist are in the radio text), with an editable command.
- Remembers the playlist, carts and settings between runs.
- **Never off air** (Settings → Safety):
  - **Silence detection**: if nothing is heard for a set time (default 15 s under -45 dB) while an item plays, AUTO
    skips to the next item and a red SILENCE warning shows. After three silent items in a row it stops skipping
    and only warns, because the fault is then the sound card or the output rather than the files.
  - **Fill music**: when the playlist runs out in AUTO, songs are added from chosen library categories (not the
    same song within 3 hours or the same artist within an hour, the longest-rested first, using every studio's
    play history), or from a folder. With the library, the folder is the backup while the library can't be
    reached.
  - **Crash-safe**: settings and the playlist are saved every few seconds, written so that a power cut can't corrupt
    them (with a backup copy). After a crash, power cut or restart the app carries on with the item after the one
    that was on air, and turns streaming back on.
  - **Watchdog**: the app runs under a small supervisor process that starts it again if it crashes or its audio
    engine stops responding. Run with `--no-watchdog` to turn it off for one run.
  - **Start at login** (Windows, Linux, macOS). For an unattended studio computer, also turn on automatic login
    and turn off sleep.
  - **Stream reconnect**: a lost or refused connection to the streaming server is retried (5 s, 10 s, … up to every
    60 s) until you switch streaming off.
  - **Log**: what went on air, warnings, errors and restarts, one file a day in the `logs` folder next to the
    settings, kept for 90 days.

### Keyboard shortcuts

Play, Pause, Next, Stop, the mic, streaming on/off and each of the 12 carts can be given any key or combination in
**Settings → Shortcuts** (click a box, press the key). Cart keys play or stop the carts of the cartwall on screen,
and each cart shows its key in its corner. Plain keys don't fire while you type in a text box. The defaults:

| Key | Action |
| --- | --- |
| F5 / F6 / F7 / F8 | Play / Pause / Next / Stop |
| F9 (hold) | Microphone open, music ducked (web radio) |
| (none) | Stream on/off, carts 1–12 |

These are fixed:

| Key | Action |
| --- | --- |
| Ctrl/Cmd + O | Add files |
| Ctrl/Cmd + S | Save playlist |
| Ctrl/Cmd + E | Edit cue points of the selected item |
| Ctrl/Cmd + T | Edit tags of the selected item |
| Ctrl/Cmd + ↑ / ↓ | Move selected playlist items |
| Ctrl/Cmd + , | Settings |
| Delete | Remove selected items |
| Enter | Put the selected item on air |

### Item types

The type icon in the playlist comes from the folders a file is in:

| Folder name contains | Type |
| --- | --- |
| `jingles`, `ids`, `sweepers`, `stingers`, `vinhetas` | Jingle |
| `ads`, `spots`, `commercials`, `comerciais`, `publicidade` | Commercial |
| anything else | Music |

So `Music/Jingles/Station ID.mp3` is a jingle. This is a first version, and category rules in the music library
will replace it later. Items put in the playlist by the schedule have their own types wherever the files are:
adverts are commercials, the jingles in a break's running order are jingles, and what events play (news,
shows) is a **programme**, which RDS, the website and the music reports don't treat as a song.

### Cue points

Right-click a playlist item (or a file in the music browser) and choose **Edit cue points…**, or press Ctrl/Cmd+E.

| Marker | What it does |
| --- | --- |
| **Cue In** | Playback starts here, which skips silence or a count-in. |
| **Intro** | End of the intro. While it runs, a green **INTRO** countdown shows how long you can talk. |
| **Outro** | Start of the ending. From here a purple **OUTRO** countdown runs to Cue Out. |
| **Mix** | The next item starts here, and this one keeps playing unfaded until its Cue Out. Use it for songs that end on a hit or a long tail. With no Mix marker, the normal crossfade (Settings → Audio) applies. |
| **Cue Out** | Playback stops here, which cuts silence or a long fade at the end. In ASSISTED mode the deck stops here. |

In the editor, pre-listen on the **Preview** output (Settings → Audio). Click the waveform to jump, and use Space to
play or pause and ←/→ to move 1 s (hold Shift for 0.1 s). The mouse wheel zooms. **Set** puts a marker at the
playhead, ▶ plays from 3 s before it, and you can also type times such as `1:23.4`. **Auto-detect** sets Cue In
and Cue Out at the first and last sound, and Mix where a fade-out has died down.

Cue points are saved per file in the music library (your audio files are never modified), so studios sharing a
library share them: an edit in one studio reaches the others within half a minute. A file from outside the music
folders is added to the library for this. Each computer also keeps its own copy in `cues.json` next to the
settings, which is used while the library can't be reached. Cue points apply wherever the file is used, and
changes to the item on air take effect straight away.

> Give Preview its own output, such as the desk's PFL/cue input or a headphone card. If it shares an output with
> Music or Cartwall, the editor warns you that what you play will be heard on air.

## Building

### 1. Install the .NET 10 SDK

| OS | How |
| --- | --- |
| Windows | `winget install Microsoft.DotNet.SDK.10`, or the installer from <https://dotnet.microsoft.com/download> |
| macOS | `brew install --cask dotnet-sdk`, or the installer from <https://dotnet.microsoft.com/download> |
| Linux | Your distribution's `dotnet-sdk-10.0` package, or the install script:<br>`curl -sSL https://dot.net/v1/dotnet-install.sh \| bash -s -- --channel 10.0`<br>then `export PATH=$HOME/.dotnet:$PATH DOTNET_ROOT=$HOME/.dotnet` |

Check it with `dotnet --version` (it should print 10.x).

### 2. Download the BASS libraries

BASS isn't committed to the repository. Fetch it from un4seen.com with the script for your OS:

```bash
# macOS / Linux (fetches the libraries for the OS you're on)
./scripts/fetch-bass.sh

# Windows (PowerShell)
.\scripts\fetch-bass.ps1
```

They are saved to `native/linux-x64`, `native/osx` or `native/win-x64`. To build for another OS as well,
pass the platform: `./scripts/fetch-bass.sh win`, or `all` for all three. PowerShell uses
`-Platform win|osx|linux|all`.

### 3. Run it

```bash
dotnet run --project src/Playout.App
```

To try it without sound (BASS's "no sound" device), run `dotnet run --project src/Playout.App -- --device=0`.
This sends every output to that device and ignores the routing in Settings.

### 4. Make a release build

`./build.sh` builds ready-to-ship packages for Windows and Linux (and, on a Mac, macOS) in `dist/` and fetches any missing BASS libraries
first: a `.zip` and a `-setup.exe` installer for Windows, and a `.tar.gz`, a `.deb` (Debian, Ubuntu, Mint…) and an
`.rpm` (Fedora, RHEL, Rocky, AlmaLinux, openSUSE…) for Linux. The installer needs NSIS (`sudo apt install nsis`) and
the `.rpm` needs rpmbuild (`sudo apt install rpm`); without them the script skips that step. Install the `.rpm` with
`sudo dnf install ./radio-playout-1.0.0-1.x86_64.rpm` (or `zypper install`).

Use `./build.sh win` or `./build.sh linux` for one platform, and
`./build.sh all 1.0.0` to set the version (by default it comes from `git describe`).

To publish a GitHub Release, commit and push your changes, then run `./release.sh 1.0.0`. It builds all five packages,
tags the commit `v1.0.0` and uploads them to the repository's Releases page. It needs the
[GitHub CLI](https://cli.github.com/) (`sudo apt install gh`, then `gh auth login`).

#### macOS

`./build.sh mac 1.0.0` builds, for Apple Silicon (`arm64`) and Intel (`x64`) Macs, **Sintoniza Radio Broadcast
system.app**, **Sintoniza Pro.app** and **Sintoniza Import.app** (which start the first's schedule program,
or it as the import program; keep them in the same folder, normally Applications), with their icons and the
microphone permission text.

- **On a Mac** (best): they are signed and put in `dist/Sintoniza-1.0.0-macos-arm64.dmg` and `-x64.dmg`, a disk image
  with a link to Applications to drag them onto. To add them to a release made with `./release.sh` on Linux: on the
  Mac, `git pull`, then `./release-mac.sh 1.0.0` (needs `brew install gh` and `gh auth login`).
- **On Linux**: they go in `.zip` files. An Apple Silicon Mac only starts signed apps, so these are signed ad hoc if
  [`rcodesign`](https://github.com/indygreg/apple-platform-rs) is installed; otherwise sign them once on the Mac after
  copying them to Applications: `codesign --force --deep --sign - "/Applications/Sintoniza Radio Broadcast system.app" "/Applications/Sintoniza Pro.app" "/Applications/Sintoniza Import.app"`.

Without an Apple Developer ID the apps are signed *ad hoc*: the first time, macOS says it can't check the developer;
right-click the app → **Open** (or System Settings → Privacy & Security → **Open Anyway**), once. With a Developer ID
(Apple Developer Program), `MAC_SIGN_IDENTITY="Developer ID Application: …" ./build.sh mac 1.0.0` signs them for
distribution, with the hardened runtime; then notarize the `.dmg` with
`xcrun notarytool submit dist/Sintoniza-1.0.0-macos-arm64.dmg --keychain-profile <profile> --wait` and
`xcrun stapler staple` it, and they open without the warning. `scripts/make-icns.py` remakes the icons.

To publish by hand instead, build a self-contained release, which needs no .NET installed on the target machine:

```bash
# Windows (64-bit)
dotnet publish src/Playout.App -c Release -r win-x64 --self-contained -o dist/win-x64

# macOS: Apple Silicon and Intel
dotnet publish src/Playout.App -c Release -r osx-arm64 --self-contained -o dist/osx-arm64
dotnet publish src/Playout.App -c Release -r osx-x64   --self-contained -o dist/osx-x64

# Linux (64-bit)
dotnet publish src/Playout.App -c Release -r linux-x64 --self-contained -o dist/linux-x64
```

You can publish for any OS from any OS, provided you have fetched that platform's BASS libraries (step 2).
The build copies the matching libraries into the output folder automatically.

Then start the app:

- **Windows:** `dist\win-x64\RadioPlayout.exe`
- **macOS:** `dist/osx-arm64/RadioPlayout` (for an app to install, use `./build.sh mac`, above). The build is
  unsigned, so if macOS blocks it, run `xattr -dr com.apple.quarantine dist/osx-arm64` once.
- **Linux:** `dist/linux-x64/RadioPlayout`. Audio goes through ALSA, which works with PulseAudio and PipeWire.

## Using it

**The user manual (PDF, with screenshots):** [English](docs/manual/Sintoniza-Manual.pdf) ·
[Português](docs/manual/Sintoniza-Manual-PT.pdf) — each
program step by step: first setup, the on-air screen, Sintoniza Pro with the daily playlists, Sintoniza Import,
everyday tasks and what to do when something isn't right. The same in text: [docs/user-guide.md](docs/user-guide.md).

It is installed with the program: Settings → About → **User manual** opens it in the program's language; Windows has
it in the Start menu, the macOS disk image next to the apps, and Linux in `/usr/share/doc/radio-playout` with a
"Sintoniza Manual" entry in the app menu.

To make the manuals again after a change (a demo station is built in `/tmp/sintoniza`, every window photographed
headless, then printed to PDF; needs ffmpeg, python3 with Pillow and Chromium): `docs/manual/make-manual.sh` (both),
or `docs/manual/make-manual.sh en` / `pt`.

## Project layout

```
src/Playout.Engine   Audio engine: BASS, main mixer, playlist automation, carts, streaming, waveforms.
                     Runs on its own thread; the UI only calls its public API.
src/Playout.Library  The database (SQLite / MySQL): music library, schema, backups, and the folder scanner.
src/Playout.Import   radio-import: command-line library scanner.
src/Playout.App      Avalonia desktop UI (deck, playlist, music browser, cartwall, settings).
scripts/             fetch-bass.sh / fetch-bass.ps1: download the BASS native libraries.
native/<platform>/   BASS libraries (downloaded, not committed).
```

The engine never depends on the UI. Automation (auto-advance, crossfades) runs from BASS sync callbacks on the
engine thread, so a frozen window can't cause dead air.

Settings are stored in `settings.json` in the per-user app data folder:
`%APPDATA%\RadioPlayout` on Windows, `~/.config/RadioPlayout` on Linux, and `~/Library/Application Support/RadioPlayout` on macOS.
The same folder has the schedule (`schedule.json`), the as-run log (`as-run`), the logs (`logs`), cue points
(`cues.json`) and measured loudness (`loudness.txt`).

## Roadmap

- Auto cue (silence trimming) for files without cue points
- The playout loading the daily logs (and reporting what aired or was missed), then a scheduling program with
  screens for clocks, the week grid, advertisers, campaigns and checking each day's log
- Live input switching and GPIO, voice tracking
- RT+ over UECP (it is sent with ASCII commands now)
- REST API for remote control

## Licensing

- **BASS** is free for non-commercial use. Commercial use, including running it at a commercial station or
  selling the software, needs a licence from [un4seen](https://www.un4seen.com/).
- **Avalonia** and **ManagedBass** are MIT licensed.
- **TagLib#** (tag editing, library scanning) is LGPL 2.1.
- **Microsoft.Data.Sqlite**, **MySqlConnector** and **System.IO.Ports** (RDS on a serial port) are MIT licensed,
  **Dapper** is Apache 2.0, and SQLite itself is public domain.
- The licence for this project's own code hasn't been chosen yet.
