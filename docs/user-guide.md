# User guide

Sintoniza is four programs and a command, sharing one database (the **library**: music, categories, the schedule,
the daily playlists and the play history):

| Program | Who uses it | What for |
| --- | --- | --- |
| [**Sintoniza Emi**](#1-sintoniza-emi-on-air) | the host, in the studio | what goes on air: the playlist, cartwall, microphone, streaming |
| [**Sintoniza Pro**](#2-sintoniza-pro) | programming, the commercial department, whoever runs the station | the station's settings, categories and cartwalls; events, ad breaks, clients and campaigns, hour templates, and the **daily playlists** song by song |
| [**Sintoniza Import**](#3-sintoniza-import) | whoever looks after the music | bringing folders of music into the library, in categories |
| [**Sintoniza Rec**](#4-sintoniza-rec) | the technician, the station's management | records what the station broadcasts and keeps it (the law asks for 30 days); exports a stretch for the regulator, a client or a complaint |
| [**radio-import**](#5-radio-import-command-line) | the technician | the same scan from the command line, e.g. every night |

Each has its own entry in the app menu (Linux), the Start menu and desktop (Windows) or Applications (macOS). Only one
copy of each runs on a computer. With a MySQL / MariaDB library, Pro and Import can run on other computers (the
commercial department, a music office): the studio sees their changes within seconds.

The **on-air program's Settings** only has what belongs to that computer (sound cards,
microphone, the database, keys); **everything about the station is set in Sintoniza Pro**, once for every
studio.

The screens follow the language chosen in the on-air program's Settings → General (English, Português, Français).

**Users.** Every program asks for a **user and password** — the on-air program, Pro, Import, the on-air program's
Settings and the web remote — the station's users, the same in every studio (see
[Users and permissions](#users-and-permissions)). A new library has two: **admin** / **admin** (may do everything)
and **dj** / **dj** (a presenter: on air, voice tracks and the web remote). Change those passwords first (Pro →
Station → Users); until then Pro's Home reminds you.

---

## First setup

On each studio computer, in the on-air program's **Settings** (the button bottom left, or Ctrl/Cmd+,; once the
library is set up it asks for a user with *This computer's settings*, e.g. admin):

1. **Library** → where the library lives: *SQLite* (a file on this computer) or *MySQL / MariaDB* (a server several
   studios share). Click **Test**.
2. **Audio** → the sound card and channel pair for **Music**, **Cartwall** and **Preview** (Test plays a short tone on
   each). Preview is for listening before air: headphones or the desk's PFL/cue, never an on-air output.
3. **Safety** → *Carry on playing when the app starts, if it is in AUTO*, *Start the app again if it crashes*, *Start
   the app when I log in*, and the daily backups.

Then once, for the whole station, in [**Sintoniza Pro**](#2-sintoniza-pro) (log in as **admin** / **admin**):

4. **Station settings** → name, logo, *FM* (mic through the desk) or *Web radio* (mic through the computer),
   crossfades, silence detection and **fill music** (what plays if the playlist ever runs out), time signal,
   streaming, RDS texts, website, alerts.
5. Put the music in categories with **Import music** (or [Sintoniza Import](#3-sintoniza-import)), make the
   **Cartwalls**, then build the hour templates and ad breaks.
6. Back in the on-air program: switch to **AUTO** and press **Play**. From then on the playout plays the daily
   playlists, the ad breaks and events at their times, and keeps each hour on time.

The first studio started after updating to this version publishes its own settings as the station's; from then on
they are changed in Pro only.

---

## 1. Sintoniza Emi (on air)

### The screen

- **Top left — the deck.** What is on air, with its waveform, **ELAPSED** / **REMAINING**, the **INTRO** countdown
  (talk over the intro), **OUTRO** and **MIX** points, and **NEXT**. The badge shows *ON AIR*, *STOPPED*, *SILENCE*
  (nothing heard although something plays) or *BREAK IN mm:ss* before an ad break.
- **Transport:** **Play** (F5), **Pause** (F6), **Next** (F7: crossfades to the next item now), **Stop** (F8).
  **AUTO** runs through the playlist; **ASSISTED** stops after each item until you start the next.
- **Top right — the station.** Clock, *TOP OF HOUR* countdown, *PLAYLIST ENDS*, *PLAYED TODAY*, *NEXT EVENT*,
  stream time, and the level meters. **STREAM** starts and stops the internet stream (server: Pro → Station settings → Streaming).
- **Bottom left — the playlist.** **Bottom right — Cartwall / Library / Folders** tabs.
- **Status line** (bottom): **Settings**, **Reports**, **Log out**, and the last thing that happened (also in the log,
  Settings → Safety → Open log folder).

### Logging in

The program opens **locked**: the on-air screen shows behind the log-in, but nothing can be clicked and the keys do
nothing until a user with the *On air* permission logs in (e.g. **dj**). Locked or not, the station stays on air:
when the program starts in **AUTO** it plays straight away (Settings → Safety → *Carry on playing when the app
starts*), the schedule and the ad breaks run, and the web remote works. **Log out (name)** at the bottom locks it
again, e.g. between shows. While locked, the window can't be closed (shutting the computer down or an update still
closes it). If the library can't be reached there are no users to check: the program opens without a log-in, so the
station can still be run, and says so on the status line.

### The playlist

- Each row: number, type icon (♫ song, ✦ jingle, € advert, 🎙 programme, stream), title and artist, **START**
  (expected start time) and **LENGTH**. Non-songs have their own background (colours in Pro → Station settings → Playlist). The
  item on air fills with red as it plays; a line marks where each hour starts.
- **This hour** (on by default): the hour before, this hour and the first 30 minutes of the next (Settings →
  Playlist). Off: everything still to play. What played before the previous hour is never shown (it is in Reports).
- A planned song that would start after its hour has ended shows **WON'T PLAY**: in the last minutes of the hour
  the playout leaves it out, so the next hour starts on time (the song on air always plays to its end; adverts,
  breaks and locked items are never left out).
- The item on air stays at the top. If you scroll away, **On air** lights up: click it to jump back.
- Songs come from the **Library** tab on the right (double-click). **Stream…** another station's stream,
  **Open… / Save…** M3U playlists, **▲ ▼** move, **Remove**, **Clear** (keeps the item on air and what the schedule
  locked). Ctrl/Cmd+O still adds files from anywhere.
- **Double-click** (or Enter) an item to put it on air now. **Drag** rows to reorder, or drop files from the
  library, the folders tab or your file manager.
- Right-click an item: *Edit cue points…* (Ctrl/Cmd+E), *Segue with the next…* (Ctrl/Cmd+G), *Edit tags…*
  (Ctrl/Cmd+T), *Remove* (Delete).
- **Segue editor:** the end of one song above the start of the next, joined where the next starts. Drag either song
  (or nudge the first one's **Mix** and **Cue Out** and the next one's **Cue In**) and **Play the join** — exactly as
  on air, on the Preview output. Moving the Mix makes a Mix marker (the first song then plays on at full level to its
  Cue Out); *Automatic* goes back to the usual crossfade. Saved as the songs' cue points, for every studio.
- Rows tagged **SCHEDULED** are ad breaks and events coming up (from Sintoniza Pro), shown where they will play.
- **Adverts are protected:** while one is on air, Next, Pause, Stop and putting another item on air ask first, and
  only a click on the button in the question does it.

Where the songs come from: the **daily playlists** made in Sintoniza Pro. The playout puts each hour in the
playlist about half an hour before it plays, and near the end of every hour it keeps time: when the next hour would
start late it leaves out an *optional* item, when it would start early it adds songs from the hour's template. What
it does is written back to the plan, so Sintoniza Pro shows what really plays. You can still change the playlist
on air; the plan isn't changed by that, and the as-run log has what really played.

### Cartwall

- Click a cart to play it; click again to stop. Carts play over the music on the Cartwall output. Each cart shows how
  long it lasts, and while it plays the time left.
- Several cartwalls: choose one in the list next to the tab. Keys for carts: Settings → Shortcuts.
- The cartwalls are the station's: they are made in Sintoniza Pro → **Cartwalls**, and every studio gets them
  within a minute.

### Library tab (Ctrl/Cmd+F)

- Search every song as you type (artist, title, album, file name; case and accents ignored). Filter by category
  (a category includes the ones under it).
- **Double-click** or Enter: the song goes to the end of the playlist and is selected there. Right-click: *Add to
  playlist*, *Play next*, *Category*, *Also in* (a song can be in several categories), *Tempo*, *Energy*, *Voice*,
  *Language…*, *Never pick automatically*, *Edit tags…*, *Edit cue points…*.
- **✎** next to the filter: the categories editor. **Scan**: bring the library up to date now.

### Folders tab

Browse the music folders on disk; drag files or folders to the playlist. Right-click a folder → *Category* puts all
its songs in a category (and new songs put there later).

### Cue points (Edit cue points…)

Set **Cue In** (where it starts), **Intro** (end of the intro), **Outro**, **Mix** (where the next item starts) and
**Cue Out** on the waveform. *Auto-detect* finds the first and last sound and a good Mix point. Space plays and
pauses on the Preview output; ← → move by a second (Shift: 0.1 s); the mouse wheel zooms.

### Web remote

Settings → **Remote**: switch it on (the port is 8090 by default) and open the address it shows on a phone or
computer on the station's network. Log in with a station user who has the *Web remote* permission (e.g. **dj**; the
phone remembers the user name). The page is laid out as the on-air screen (on a phone, one panel under the other):
the deck with the waveform, time played and left, the talk-over and ad break countdowns, **▶ ❚❚ ⏭ ■** and **AUTO**;
the station panel with the clock, top of hour, playlist end, next event and the level meters; the playlist with its
colours and tags (tap an item for *▶ Now* and ✕); and **CARTWALL** (any of the cartwalls) / **LIBRARY** (search, add
a song after the item on air). An advert on air is only
cut after a question. Five wrong passwords lock it for a minute. It is plain HTTP: for the station's own network, or
from outside only through the station's VPN — never open the port on the router.

### Microphone (web radio)

With *Web radio* chosen (Pro → Station settings), **MIC** (F9) opens the microphone: music and carts fade down while it
is open. Hold to talk, or press on / press off (Settings → Microphone). With *FM radio* the mic goes through the desk.

### Reports

**Reports** (bottom left): *Music played (royalties)*, *Music totals per song*, *Adverts (proof of broadcast)* (per
advertiser) and *Everything (as-run log)*, for a range of days. Save as CSV (commas or semicolons) or as a page to
print / save as PDF.

### Settings at a glance

This computer's only (the station's are in Pro → Station settings). Settings asks for a user with *This computer's
settings* (once logged in it stays open for 5 minutes); while the library can't be reached it opens without asking, so
the database can be fixed.

| Tab | |
| --- | --- |
| General | the language of the screens |
| Audio | outputs for Music / Cartwall / Preview |
| Microphone | (web radio) input, levels, how far the music goes down |
| Safety | start-up and crash behaviour, log folder, **Backups** |
| Library | the database, automatic scans |
| Shortcuts | keys for transport, MIC, stream and carts |
| RDS | whether this computer sends to the RDS encoder, and how it reaches it |
| Playlist | how much of the next hour the playlist shows |
| Remote | the web remote: on/off, port, its address (it logs in with the station's users) |
| About | version, updates, the manual |

### Updates

When a new version is out, a green **Update available** button appears (Settings → About to check now). Click it to
install (Windows: the app restarts on the new version; Linux: it asks for the password, then offers a restart).

---

## 2. Sintoniza Pro

The station's production program, laid out as a workstation: a **menu** (File, Library, Programming, Traffic,
Station, Help) and a **toolbar** at the top; the **tools** on the left (Home · Library: songs, categories, import ·
Programming: hour templates, daily playlists, events · Traffic: ad breaks, clients and campaigns · Station: settings,
cartwalls, users), with **Now playing** (from every studio's play history, and this hour's template) and the **station**
box under them; the chosen tool in the middle; a **status bar** at the bottom (version, station, database, user,
unsaved changes, date and time). What is changed in another program or on another computer (an import, new categories, new songs)
shows by itself within half a minute; **⟳ Refresh** in the toolbar (F5) reads it all again now.

It opens on **Home**: the clock, what is on air, **today** (hours planned, ad breaks and adverts booked, events,
voice tracks), what **needs attention** (missing files, songs without a category, hours without a template,
campaigns ending this week, unsaved changes), the **library**, the **database** and **this computer** (system,
processor, memory, free disk, how long it has been on). **Songs** shows the whole library (search, categories,
tempo and energy, tags, listen).

### The station (Station and Library, on the left)

- **Station settings** — name and logo, genre, website, FM or web radio, money sign; crossfades and loudness; time
  signal; silence detection and fill music; other music folders; streaming server; RDS texts (PS, radio text, RT+);
  website now playing; alerts; **takes** (below); days of daily playlists made ahead and row colours. Saved for every studio: each
  playout takes them within a minute.
- **Categories** — the library's categories. Every station has the same eight at the top, which can't be renamed,
  moved or deleted: **Music**, **Jingles**, **Promos**, **ProgAutor** (shows recorded outside), **Fillers**,
  **Redação** (news), **Spots** (adverts) and **Takes** (voice inserts: the time, the temperature… as if a host were
  there). Make your own under them (*New under it*: Music › 80s, Spots › Clients…), each with its colour and folders.
  **⟳ Scan its folders now** (with a category selected) looks for songs added to or taken out of its folders straight
  away; the on-air program also watches those folders and scans them by itself.
- **Cartwalls** — the cartwalls every studio plays: New, Copy, Rename, Delete; click a cart to give it a file
  (or drop one on it), a colour and an icon, and listen to it.
- **Import music** — Sintoniza Import, inside Pro.
- **Users** — the station's users and what each may do (below).

### Users and permissions

Pro opens with a log-in; File → **Switch user…** changes the user, File → **Log out** saves the changes and covers Pro
with the log-in until someone logs in (or **Close**). Each user has a name, a password and the
**permissions** ticked for them; Pro shows only the tools, menus and buttons they may use:

| Permission | Lets them |
| --- | --- |
| On air | the on-air program in the studio: playing, the playlist, the cartwall, the microphone |
| Library | songs, categories, importing music (and Sintoniza Import) |
| Programming | hour templates, daily playlists, events |
| Voice tracks | record voice tracks (they see the daily playlists, but can't change the songs) |
| Traffic | ad breaks, clients and campaigns |
| Station | the station's settings and cartwalls |
| Users | add and change users |
| This computer's settings | open a playout's own Settings (sound cards, database, keys…) |
| Web remote | control a playout from a phone |

**Everything** and **Presenter** (on air, voice tracks and web remote) tick the usual sets. **New user** → a name, a password
→ **Save user**; to change a password type a new one (empty keeps it). Untick *May log in* to stop someone without
deleting them. You can't delete or lock out yourself, nor take away your own *Users*. Passwords are kept only as a
secure hash (PBKDF2): a forgotten one is set again by another user with *Users*.

### ERC quotas (Portuguese music)

The Lei da Rádio (Lei n.º 54/2010, changed by Lei n.º 16/2024, art. 41.º–47.º) asks, of the songs a station plays in a
month: at least **30 %** Portuguese music; of the Portuguese music, at least **60 %** in Portuguese performed by EU
citizens and at least **35 %** first edited in the last 12 months. The same percentages must also be met **Monday to
Friday from 7:00 to 20:00**. Every month the station sends the ERC the list of what it played (art. 47.º-B), through
its portal (portalradios.erc.pt).

**Classifying the songs.** Each song has: *Portuguese music* (yes / no), the *language* it is sung in (pt-PT, pt-BR,
en…), the *performer's country* and its *first edition* (yyyy-mm-dd, yyyy-mm or yyyy; empty: the year tag). Give them
to a whole category in **Categories** (e.g. Music › Portuguese: Portuguese music, pt-PT, Portugal): its songs, new
ones too, and the categories under it take them. A song can say otherwise: **Songs** → select → right-click → **ERC
classification…** (several at once: what differs between them can be left as it is).

**Pro → Programming → ERC quotas** shows, for a period (*Last month*, *This month* or any dates), the three quotas for
the whole period and for Monday–Friday 7–20h, green when met and red when not, with how many songs of how many.
When the period isn't over yet it also shows them **with the daily playlists still to play**, so a shortfall can be
fixed before the month ends. **Songs to classify** lists what went on air (or is planned) without the ERC's details —
until classified they count as not Portuguese — select them and **Classify…**.

**Export for the ERC (XML)…** saves the file the ERC's portal takes: `ERCRATIO`, the station's **ALIAS**, a
`PLAYLIST` a day with each song's time, duration, artist, title, nationality, language, first edition, whether it is
Portuguese music and ISRC (ISO-8859-1). On the right: the programme service's name at the ERC (ALIAS) and the
**countries with their ERC codes** (the ERC numbers countries its own way: Portugal is 045; copy the others from the
portal), and which are in the EU.

**Planning to the quotas.** With *Make the daily playlists meet the quotas* ticked (on the right; on by default), each
hour of the daily playlists is made to meet the three quotas on its own (a little above the minimums): when it has too
little Portuguese music, the next music line picks a Portuguese song (about one in three), and among those, songs in
Portuguese by EU performers and recent ones where those fall short — from the classified songs of each line's
category, as the rest rules allow. So Monday–Friday 7–20h is met too. A line whose category has no such songs is left
as it is; put Portuguese music in the categories your templates use.

### Song restrictions

**Songs** → select → right-click → **Restrictions…**: when a song may be picked automatically —

- **only from … until …** (a Christmas song in December; a new release from its release date);
- **on these days** and **at these hours** (no ballads before 10:00: untick 00–09);
- **at most N times a day** and **N times a week** (Monday to Sunday).

The daily playlists keep them always (a line whose songs are all restricted at that time is skipped and reported);
fill music keeps the dates, days and hours. Put in by hand (Daily playlists, the on-air playlist), a song plays all
the same. Songs with restrictions show ⏱ in the list; hover for what they are.

### Takes (the time and the temperature)

Takes are short recorded inserts the playout puts together **when they go on air**, so they are always right.
**Station settings → Takes**: the **folder** with them, and where the station is (latitude and longitude) for the
temperature, read every 10 minutes from Open-Meteo (free, no account).

| File | Says |
| --- | --- |
| `HRS00` … `HRS23` | the hour |
| `MIN01` … `MIN59` (`MIN00` optional) | the minutes |
| `HRS10_15` (optional) | the whole time in one file |
| `TMP0` … `TMP45`, `TMPM1` … `TMPM10` | the temperature (M: below zero) |

Any audio format; several recordings of the same (`HRS10a`, `HRS10b`) are taken at random. The time said is the
nearest minute when the take starts. In an hour template: **Add a line → A take ▾** → *The time*, *The temperature* or
*The time and the temperature*. A take whose files are missing is skipped; without the temperature, a time-and-
temperature take says the time only.

### The schedule

Five tools, on the left under Programming and Traffic. **Save** (in the toolbar, lit when something changed, or Ctrl+S) writes it to the database; the playout takes it
within a few seconds. If someone else saved meanwhile, you are asked before replacing theirs.

The calendars (Events, Ad breaks, Playlists) are a week grid: **click a cell** to place the selected item at that day
and time (click again to take it out), **drag** to do many cells, **click a time** for every day. *Rows every* and
*from minute* change the grid; **Add row** adds an exact time (e.g. `12:59:55`; `*:59:55` = that minute and second of
every hour, every day).

### Events

Something that plays at set times: a file, the **newest file in a folder** (e.g. the news, skipped if older than N
minutes), or an internet stream for N minutes. Choose how it starts:

- **Soft:** after the item on air ends (at most N minutes late).
- **Hard:** on the second (what is on air fades out).
- **Ends at its time:** it ends exactly then, to the second — never later. The songs before it are adjusted; if the
  music would still run over, what is on air is faded at the moment the break has to start; if it would get there early,
  a short gap (up to 2 minutes: Station settings → Safety) is filled from the **Fillers** category (beds, short
  instrumentals), and a longer one with a song (from the station's music, even with fill music off) faded when the
  break starts. A break that ends at 10:00 is the last of the 09:00 hour (the daily playlists leave room for it there,
  and leave its end to it). With the time signal set to stop what plays, a break that ends on the hour ends as the pips
  start (09:59:54); after the pips, what was left of the 09:00 hour doesn't play: the 10:00 hour starts.

and what the host may do with it in the playlist (remove/move, only move, or nothing).

### Ad breaks

A break has a **running order**: jingles (**Jingle…**) and **sections** (e.g. *Before the hour*), where the adverts
booked in that section go. *Play this jingle only when the section after it has adverts* makes a section opener. A
break with nothing booked is skipped. Place breaks on the calendar; start and lock options as for events (breaks are
locked by default).

### Clients

- **Clients** (the name on the proof of broadcast) → their **Campaigns**: the advert files (several take turns), the
  **section**, the **position** (automatic, 1–5 from the start, 6–10 at the end), first and last day.
- The week grid shows the ad breaks; **click a break** to book the campaign there *every week* or *on that date only*.
  **In every break** books it in every break that has its section. **Remove the places with no break** tidies up
  after breaks were moved.

### Playlists (hour templates)

A template is an hour's recipe: **lines played in order** (then from the top again until the hour is full). A line is
a song from a **library category** (Music, Gold, Music › 80s…; jingle and advert categories work too) or a file from
a **folder**. Place the template on the calendar: each row is an hour (drag to do many; click an hour for every
day). This is how you set up your shows: e.g. *Manhãs* on weekdays 07–10, *Tardes* 14–19.

**Music rules.** Select a line to ask for its songs' **tempo** (1 slow … 5 fast) and **energy** (1 calm … 5 lively),
e.g. tempo 4–5 for the first song after the news. Set both on the songs in the library (select many, right-click →
*Tempo* / *Energy*; hover a song to see them). For the whole template:

- **Same artist not again within N hours** (0 = the artist rest time in Station settings → Safety).
- **Same album not again within N hours** (0 = no album rule).
- **Slow songs in a row, at most N** (slow = tempo 1–2; 0 = no limit).

The same song never comes back within the song rest time (Station settings → Safety). When a rule would leave nothing to play
it is let go for that song — the album first, then slow songs, then the artist — so the hour is always full; a line
whose tempo or energy no song has plays from its whole category, and Daily playlists says so.

### Daily playlists

The hours as they will play, made from the templates:

- **The day** (◀ ▶, the date, **Today**) and its **24 hours** on the left, with each hour's template and length.
  ✎ = changed by hand (never made again by itself); ↻ = its template changed since it was made.
- **The hour**: every song and jingle in order with its start time and where it came from (template line and
  category), and the ad breaks (each advert with its client) and events in their place. The total is at the bottom.
  Items marked **OPTIONAL** play only if there is time. An hour made longer than the hour by hand marks the songs
  that would start after it ends **WON'T PLAY**: the playout leaves them out so the next hour starts on time.
- **What played**: ✓ and dimmed; the item on air **▶**, with a line saying how far ahead or behind the plan the
  playout is. Songs the playout added to keep time say *added on air to keep time*.
- **Build it song by song:** **Another song** (another pick from the same line), **▲ ▼**, **Remove**, or double-click
  a song in the library on the right to put it after the selected item. Each change is saved at once and marks the
  hour ✎.
- **Streams at a set time:** **Stream…** puts another station's stream in the hour: its address, when it starts
  (e.g. 14:00:00), on the dot (what is on air fades out) or after the item on air, and how long it plays. It is saved
  in the schedule as an event for that day only (Events lists it as *once: 20/10 14:00*), so the playout starts it on
  time like any event. Select it and **Remove** to take it out.
- **Or let your rules do it:** **Make this hour again** / **Make the day again** (hours changed by hand are kept,
  unless you make that hour again on purpose). Today and the next days (Station settings → Playlist)
  are always made; an hour whose template changes is made again within minutes unless it was changed by hand or is
  about to play.
- **Listen:** select an item and press **▶** (or double-click, or Space); right-click a library song → *Listen*. It
  plays on the Preview output only; a red warning appears if Preview is set to the on-air output.
- **Voice track…** (select a song): record your talk after it, as if you were live. **Start** plays the song's end;
  **Talk** (Space) starts recording — over its ending if you like; **Next song** (Enter) brings in the following item,
  under your voice if you keep talking; **Stop** (Space) ends the take. **Play it back** as on air, nudge where it
  starts over the song and where the next one comes in, **Record again** or **Save**. It is kept as an MP3 in the
  station's voice tracks folder (Station settings → Library; asked the first time) and in the library under
  **Takes**, and goes right after the song, shown 🗣. A voice track belongs to its song: removing or replacing the
  song takes it too, moving the song moves it, and on air it is skipped if the song can't play.
- **Segue…**: the segue editor for the selected item and the next (see the playlist above); the planned times follow.
- **Edit tags…** of the selected item (or right-click a library song): title, artist, album… written in the file. A
  song on air right now can't be edited until it ends.

The rules the songs are picked by: the songs rested longest first, a song not again within N hours and an artist not
within N minutes (Station settings → Safety → fill music), never the same artist twice in a row while another can play.

---

## 3. Sintoniza Import

Brings a folder of music into the library, in categories. It asks for a user with the *Library* permission.

1. **Choose…** the folder (e.g. `D:\Music\Sintoniza`).
2. **Into the category**: its name (by default the folder's), **Under** (one of the station's eight — Music by
   default — or a category under them), and the **Kind** for new categories (it follows the one it goes under).
3. **Folders inside become sub-categories**: none, or 1–3 levels (`Music/80s/Rock` → *Music › 80s › Rock*).
4. **Songs already in another category**: keep it and add this one too (a song can be in several), move them, or
   leave them as they are.
5. **Preview import** shows the categories it will make (NEW or ALREADY THERE) and, for each, the files, how many
   are in the library already and how many are in another category. Nothing is changed yet.
6. **Import** makes the categories, reads the songs, puts each in its category, and keeps each folder for its
   category: songs put there later get it too (the playout scans those folders by itself). **Stop** halts it;
   importing again finishes.

---

## 4. Sintoniza Rec

The station's recorder (from *gravação*): it records what goes out, all day, every day, and keeps it. The Lei da
Rádio (art. 39.º) asks for the broadcasts to be recorded and kept at least 30 days. It doesn't need the library or a
user: it can run on the studio computer or on any other.

**What it records** (⚙ Settings → Source): an **input** of the computer — best, the desk's programme output into the
sound card, so the microphones are in it — or the station's **stream** (a web address), which it opens again by
itself if it drops. *Start Sintoniza Rec when I log in* makes it start recording on its own after a restart.

**The files** (Settings → Files): a folder with a folder a day inside it (`2026-10-21/RE_2026-10-21_10-00-00.mp3`),
a new file every 5–60 minutes from a set minute (each new file starts before the old one stops: nothing is lost),
MP3 (64–320 kbps, mono to halve the space) or WAV. Recordings older than **Kept for** are deleted by themselves; it
shows how much space a day and the whole period take. An optional **archive copy** (mono, low quality, in another
folder, e.g. a NAS) can be kept for months or always.

**The window** shows what is recorded (● REC and for how long, the file being written, the meters, the free space),
**🎧 Listen live** (to the *Listening* output: headphones, never an output that goes on air), and below, the
**recordings of a day**: pick a file and play it, or **Open folder**. **Export a stretch**: a day, a start and an end
time, MP3 or WAV → one file, cut to the second, taken from as many recordings as needed (a time the recorder was
stopped is left out and said so).

**Alerts** (Settings → Alerts): silence (or a stream that dropped) for longer than set, a recording that can't be
written, less than 2 GB free, and a "still recording" message once a day if wanted; by Telegram, e-mail or a web
address (**Send a test**). Closing the window asks first, because it stops the recording.

---

## 5. radio-import (command line)

```
radio-import                          scan the library's folders (Pro → Station settings → Library)
radio-import /music /jingles          scan these folders
radio-import --db /path/library.db    an SQLite library file
radio-import --mysql user:password@server[:port]/database
radio-import --stats                  what is in the library
```

Only new and changed files are read, so it can run every few minutes from cron or Task Scheduler. Deleted files and
the songs of deleted folders are taken out; a drive or share that isn't connected is left alone.

---

## How do I…

- **…set up a weekday morning show?** Pro → Playlists → New → name it, add its lines (e.g. Music, Music,
  Jingles, Gold), click the hours 07–10 Monday to Friday → Save. Check it on Daily playlists.
- **…put the adverts last in the hour?** Pro → Ad breaks → New → running order with a section → Add row
  `*:00:00` and click it for every day → choose *Ends at its time*: it ends exactly on the hour, the last thing of the
  hour before → book the campaigns on the Clients tab.
- **…change a song in tomorrow's 09:00?** Pro → Daily playlists → ▶ to tomorrow → 09:00 → select the song →
  Another song (or Remove and double-click one in the library).
- **…put a folder of songs in Gold?** Sintoniza Import with that folder, *Into the category* Gold, no sub-categories.
  Or in the on-air program: Folders tab → right-click the folder → Category → Gold.
- **…give a presenter their own login?** Pro → Station → Users → New user → their name and a password → **Presenter**
  → Save user. They can work on air, record voice tracks in Pro and use the web remote, nothing else.
- **…send the ERC last month's music?** Pro → ERC quotas → *Last month* → check the quotas and classify what is
  listed under *Songs to classify* → **Export for the ERC (XML)…** → upload the file in the ERC's portal.
- **…get the music report for the collecting society?** On-air program → Reports → Music played (royalties) → the
  month → Save as CSV.
- **…send an advertiser the proof of broadcast?** Reports → Adverts → the advertiser and dates → Save as a page to
  print → print to PDF.

## When something isn't right

- **The playlist doesn't match Sintoniza Pro.** Press **Clear** in the on-air program: the playlist refills from
  the plan, from the item planned for that moment. If it runs ahead or behind, the line above the hour in Daily
  playlists says by how much; it catches up by the next hour.
- **Only jingles and adverts play.** The songs' files have moved or been deleted: fix the categories' folders (Pro → Categories)
  and click **Scan** in the on-air program's Library tab; songs that are gone are taken out of the library.
- **A song is skipped with "FileOpen".** The file isn't where the library says: scan again.
- **Something went wrong on air.** Settings → Safety → **Open log folder**: one file a day of what played, warnings and
  restarts. Pro → Station settings → Alerts can tell you by Telegram or e-mail.
