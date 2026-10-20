# Library database

The station's database: an SQLite file on the studio computer, or a MySQL / MariaDB server that several studios
(and the schedule program on another computer) share (Settings → Library). It holds the music library, the
schedule and advertising traffic, the folders of each category, and the as-run log. The app reads and edits it, and
`radio-import` fills the library from the music folders. Other programs may read it and write to it too. This page
is the contract they rely on.

What stays on each computer: its settings (audio devices, the database login…), and, so the station keeps playing
when the database can't be reached, the playout's playlist and a copy of the last schedule it read.

Schema version **10**. The scripts that create the tables are
[`sqlite-1.sql`](../src/Playout.Library/Schema/sqlite-1.sql) and
[`mysql-1.sql`](../src/Playout.Library/Schema/mysql-1.sql), then
[`sqlite-2.sql`](../src/Playout.Library/Schema/sqlite-2.sql) and
[`mysql-2.sql`](../src/Playout.Library/Schema/mysql-2.sql) (cue points), then
[`sqlite-3.sql`](../src/Playout.Library/Schema/sqlite-3.sql) and
[`mysql-3.sql`](../src/Playout.Library/Schema/mysql-3.sql) (stations, and the tables of an earlier log-based design),
then [`sqlite-4.sql`](../src/Playout.Library/Schema/sqlite-4.sql) and
[`mysql-4.sql`](../src/Playout.Library/Schema/mysql-4.sql) (the schedule, traffic, category folders and as-run log;
the earlier design's tables are removed if they are empty), then
[`sqlite-5.sql`](../src/Playout.Library/Schema/sqlite-5.sql) and
[`mysql-5.sql`](../src/Playout.Library/Schema/mysql-5.sql) (categories in a tree, and songs in more than one
category), then [`sqlite-6.sql`](../src/Playout.Library/Schema/sqlite-6.sql) and
[`mysql-6.sql`](../src/Playout.Library/Schema/mysql-6.sql) (the daily playlists), then
[`sqlite-7.sql`](../src/Playout.Library/Schema/sqlite-7.sql) and
[`mysql-7.sql`](../src/Playout.Library/Schema/mysql-7.sql) (events once, on a date), then
[`sqlite-8.sql`](../src/Playout.Library/Schema/sqlite-8.sql) and
[`mysql-8.sql`](../src/Playout.Library/Schema/mysql-8.sql) (music rules: songs' energy, lines' tempo and energy,
templates' artist and album gaps), then
[`sqlite-9.sql`](../src/Playout.Library/Schema/sqlite-9.sql) and
[`mysql-9.sql`](../src/Playout.Library/Schema/mysql-9.sql) (the eight fixed top-level categories and the station's
settings), then [`sqlite-10.sql`](../src/Playout.Library/Schema/sqlite-10.sql) and
[`mysql-10.sql`](../src/Playout.Library/Schema/mysql-10.sql) (voice tracks in the daily playlists). The app creates the tables itself the
first time it opens an empty database, and upgrades an older one, so only the database and a user with rights to
create and alter tables are needed:

```sql
CREATE DATABASE sintoniza CHARACTER SET utf8mb4;
CREATE USER 'sintoniza'@'%' IDENTIFIED BY '…';
GRANT ALL PRIVILEGES ON sintoniza.* TO 'sintoniza'@'%';
```

## Conventions

- **Times** are UTC text in the form `2026-10-14T18:20:11Z`.
- **Booleans** are `0` / `1`.
- **Paths** are full paths to the file as the studio computers see it. With a shared MySQL library, every studio
  must see the music under the same path, e.g. the same mapped drive or mount point. Paths are compared exactly
  (case and accents count) and may be up to 760 characters long.
- `schema_info` holds `version`. A program that finds a version it doesn't know should not write. The app
  refuses to open a library made by a newer version, so studios sharing one should run the same version.

## Tables

### `categories`

What a song or jingle is for the station. Categories make a tree: *Music › 80s › Rock* is Rock under 80s under
Music. Choosing a category (the library filter, a smart playlist line, fill music) takes in the songs of every
category under it.

At the top there are always the same eight, with a `role`, which the app makes when it opens the library and which
can't be deleted, renamed or moved: **Music** (`music`), **Jingles** (`jingles`), **Promos** (`promos`), **ProgAutor**
(`progautor`: shows recorded outside), **Fillers** (`fillers`), **Redação** (`redacao`: news), **Spots** (`spots`:
adverts) and **Takes** (`takes`: voice inserts). A top-level category of the same name is taken as the fixed one
(Musicas → Music, Ads → Spots…); any other top-level category is moved under the fixed one of its kind (music →
Music, jingle → Jingles, commercial → Spots, other → ProgAutor). A program that adds categories should put them under
one of these.

| Column | Type | |
| --- | --- | --- |
| `id` | integer | |
| `parent_id` | integer, null | → `categories.id`: the category it is under; null at the top |
| `name` | text | unique among the categories with the same parent (the app also keeps top-level names unique) |
| `kind` | text | `music`, `jingle`, `commercial` or `other`: how the playout treats the category |
| `color` | text, null | `#RRGGBB`, shown in the app |
| `sort_order` | integer | order in lists |
| `role` | text, null | one of the eight above for the fixed top-level categories; null for the rest |

### `tracks`

One row per audio file.

| Column | Type | |
| --- | --- | --- |
| `id` | integer | |
| `path` | text, unique | full path of the file |
| `file_size`, `file_modified` | integer, time | size in bytes and last write time; a scan reads a file again only when these change |
| `title`, `artist`, `album`, `genre` | text | from the tags (`''` if none) |
| `year` | integer, null | |
| `isrc` | text, null | for royalty reports |
| `duration` | real | seconds |
| `category_id` | integer, null | → `categories.id`: the song's own category; null = not categorised yet. It can be in others too (`track_categories`) |
| `tempo` | integer, null | 1 (slow) … 5 (fast) |
| `energy` | integer, null | 1 (calm) … 5 (lively) |
| `voice` | text, null | `male`, `female`, `duo`, `group` or `instrumental` |
| `language` | text, null | short code: `pt`, `en`, … |
| `enabled` | 0/1 | 0 = never picked automatically (rotation) |
| `missing` | 0/1 | 1 = not found, but kept (only when the row can't be deleted). A scan takes files that are gone out of the library: deleted files, and every song of a deleted folder. Moved and renamed files are followed and keep their row |
| `added_at`, `updated_at` | time | |
| `search` | text | what search matches against. **Insert `''`**: the app fills it in (lower case, accents removed) when it opens the library |
| `cue_in`, `intro`, `outro`, `mix`, `cue_out` | real, null | cue points in seconds from the start of the file; null = not set (see the README) |
| `cues_updated_at` | time, null | when the cue points were last set or cleared, by the **database server's** clock; null = never |

The app reads the cue points of files whose `cues_updated_at` changed every 30 seconds. A program that changes cue
points must set `cues_updated_at` to the server's current UTC time in the same form
(`DATE_FORMAT(UTC_TIMESTAMP(), '%Y-%m-%dT%H:%i:%sZ')` in MySQL, `strftime('%Y-%m-%dT%H:%M:%SZ', 'now')` in
SQLite), or the studios won't see the change until they restart.

### `plays`

Every item that went on air: the as-run history. Rotation rules (artist and title separation) and reports use it.

| Column | Type | |
| --- | --- | --- |
| `id` | integer | |
| `track_id` | integer, null | → `tracks.id` when the file is in the library |
| `path`, `artist`, `title` | text | as played, so the history stays readable if the track is deleted |
| `started_at` | time | when it went on air |
| `source` | text | `playlist` or `cart` |
| `station_id`, `log_item_id`, `ended_at`, `kind` | | from schema 3, not used |

### `track_categories`

The other categories a song is in, besides its own (`tracks.category_id`): a song can be in Gold and in 80s. Writers
that only set `tracks.category_id` stay correct: a song is in a category if either says so.

| Column | Type | |
| --- | --- | --- |
| `track_id` | integer | → `tracks.id` (rows go with the track) |
| `category_id` | integer | → `categories.id` (rows go with the category) |

### `category_folders`

The folders given to a category (Settings → Library → Categories…, right-click a folder, or Sintoniza Import): their
songs are in it, and new songs found there by a scan get it too. When folders are inside one another
(`Music` and `Music/80s`), a song gets the category of the deepest folder it is in.

| Column | Type | |
| --- | --- | --- |
| `path` | text, key | the folder, as the studio computers see it |
| `category_id` | integer | → `categories.id` |

### `as_run`

What went on air, with its start and end: what the reports (music for royalties, proof of broadcast for
advertisers) are made from. Each studio also keeps its own CSV files and puts in later what the database missed.

| Column | Type | |
| --- | --- | --- |
| `id` | integer | |
| `station_id` | integer | → `stations.id` |
| `started_at`, `ended_at` | text | local time, `yyyy-MM-ddTHH:mm:ss`; `ended_at` null if the app stopped while it played |
| `type` | text | `Music`, `Jingle`, `Advert`, `Stream`, `Programme` |
| `source` | text | `playlist` or `cart` |
| `event`, `advertiser` | text | the scheduled event or ad break (`Anuncios 10:59`), and whose advert it is |
| `artist`, `title`, `album`, `composer`, `label`, `isrc`, `year` | text | from the file's tags |
| `file` | text | the file played; `station_id`, `started_at` and `file` together are unique |

## The station's settings

`station_settings` (one row per station): `settings` is a JSON object with what every studio shares — station name,
logo (base64), crossfades, loudness, silence detection, fill music, time signal, RDS texts, website now playing,
alerts, streaming server, cartwalls and the library's other folders — saved by Sintoniza Pro; `version` goes up
by one on every save and the playouts read it again; `updated_at` (UTC) and `updated_by`. What belongs to one
computer (sound cards, microphone, the database login, keys) stays in that computer's settings file.

## The schedule

What the schedule program (Sintoniza Pro) edits, for each station (`station_id`; one station, 1, for now).
It saves everything at once, as a new version in `schedule_versions`; the playout reads it again when the version
changes. Ids are text (`CHAR(36)`, a GUID). Days: `weekday` 0 = Sunday … 6 = Saturday; times `HH:mm:ss`; dates
`yyyy-MM-dd`.

| Table | |
| --- | --- |
| `schedule_entries` | `type` `event` (a file, the newest file in a folder, or a stream at set times), `break` (an ad break) or `playlist` (an hour template); `name`, `enabled`, `start_mode` (`Soft`, `Hard`, `EndAt`), `max_delay_minutes`, `lock_level` (`Free`, `Movable`, `Locked`); for events `path`, `stream_minutes`, `max_age_minutes`; for hour templates `artist_hours` (0 = the playout's artist rest time), `album_hours` (0 = no album rule), `max_slow_in_row` (songs of tempo 1–2; 0 = any); `position` keeps the order of the lists |
| `schedule_slots` | `entry_id`, `weekday`, `time`: when each entry runs, every week (for an `EndAt` break, when it ends) |
| `schedule_dates` | `entry_id`, `at` (local `yyyy-MM-ddTHH:mm:ss`): when an entry runs once, besides its weekly times (e.g. a stream put in a day's playlist) |
| `break_parts` | a break's running order: `position`, `kind` (`Jingle`, `Section`), `path` of a jingle, `section` name, `only_with_adverts` (a section opener) |
| `playlist_lines` | an hour template's lines, in `position` order: `source` `Category` (`category_id`, `category_name`, `kind`; `tempo_min`, `tempo_max`, `energy_min`, `energy_max`: the songs it may pick, null = any) or `Folder` (`folder`) |
| `ad_clients` | advertisers: `name` (on the proof of broadcast), `enabled` |
| `ad_campaigns` | a client's campaigns: `name`, `enabled`, `starts_on` and `ends_on` (null = no limit), `section` of the break they go in (`''` = its first), `place` (0 automatic, 1–5 from the start of the section, 6–10 at its end) |
| `ad_campaign_files` | a campaign's adverts (they take turns): `position`, `path` |
| `ad_placements` | where a campaign plays: the break at `time`, every week on `weekday`, or only on `date` |
| `schedule_state` | `entry_id`, `last_started`: when the playout last started each event and break (local time), so a restart doesn't start it again; not touched by a save |
| `schedule_versions` | `station_id`, `version`, `updated_at` (UTC), `updated_by`: a save over a newer version is asked about first |

## The daily playlists

Made ahead of time from the hour templates by the playout and by Sintoniza Pro (its Daily playlists tab), which also edits them; the
playout plays them. One row per hour in `plan_hours`, its items in `plan_items`. Ad breaks and events aren't stored
here: they come from the schedule, as booked at air time. Hours are local times `yyyy-MM-ddTHH:00:00`.

| Table | |
| --- | --- |
| `plan_hours` | `station_id`, `hour_start`; `template_id` (the hour template it was made from, null: none), `signature` (that template's lines then: when it changes, an hour not `edited` is made again), `generated_at` (UTC), `generated_by` (the computer), `edited` (1: changed by hand, never made again by itself) |
| `plan_items` | an hour's items in `position` order: `kind` (`music`, `jingle`, `commercial`, `programme`, `voice`), `path`, `title`, `artist`, `seconds` (how long it plays before the next starts), `track_id` (→ `tracks.id`, null if not in the library), `line` (the template line it came from, 0 = first; null: put in by hand), `category_id`, `optional` (1: at the end of the hour, played only if there is time); for a voice track (`kind` `voice`) `overlap` (seconds it starts before the item before it ends, over its ending) and `follows` (1: it belongs to the item before it — removed, moved and skipped with it) |

Another program may write an hour (its row and items in one transaction) and set `edited` to 1 so it isn't made
again. Hours from two days back are removed by the playout.

## Adding music from another program

Insert into `tracks` with at least `path`, `title`, `artist`, `duration`, `added_at`, `updated_at` and
`search = ''`. Set `file_size` and `file_modified` too if you can. Otherwise `radio-import` reads the file again the
next time it scans that folder, keeping its id, category and attributes.

## radio-import

```
radio-import                          scan the folders set in Settings → Library into the app's library
radio-import /music /jingles          scan these folders
radio-import --db /path/library.db …  an SQLite library file
radio-import --mysql user:password@server[:port]/database …
radio-import --stats                  what is in the library
```

Only new and changed files are opened, and only their tags and headers are read, so it can run every few minutes
from cron or Task Scheduler (the app also scans by itself; see Settings → Library). Several scans at once, for
example from studios sharing a MySQL library, are safe. Files that are gone are taken out of the library, and so are
all the songs of a folder that was deleted (it is gone while the folder it was in is there). A folder that can't be
reached (a drive or network share that isn't connected: the folder above it is gone too, or it is there but empty) is
skipped, and nothing in it is removed.
