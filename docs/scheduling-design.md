# Scheduling: clocks, rotation, traffic and daily logs

The music library database (see [library-database.md](library-database.md)) also holds the station's
**schedule**, from schema version **3**: clocks (the plan of each hour), rotation rules, commercial traffic
(advertisers, campaigns, bookings) and the **daily logs** made from them, which the playout plays.

```
 radio-schedule / scheduling program             Shared database                        Playout (studio)
 ───────────────────────────────────             ───────────────                        ────────────────
 clocks, rotation rules, advertisers,  ─ writes ►  clocks, rules, advertisers,
 campaigns, bookings                               campaigns, bookings
 make the next days' logs (nightly)    ─ writes ►  logs, log_items              ◄ reads ─  each hour, ahead   (step 2)
 update adverts (late bookings)        ─ writes ►                               ◄ writes ─ plays, item states  (step 2)
```

**What exists now:** the tables (schema 3, SQLite and MySQL / MariaDB), the log maker and `radio-schedule`, the
command-line tool that runs it. **Next:** the playout loading logs (step 2), and a scheduling program with screens
for clocks, the week grid, advertisers and campaigns, and checking a day's log (step 3). Until then the tables can
be filled by `radio-schedule example`, SQL or another program.

## Decisions

- **Several stations** can share one database (two frequencies of one company, or a main and a local
  station). Clocks, rules, campaigns and logs belong to a station; categories, tracks and advertisers are shared.
  A database starts with one station, "Station 1". Studios of the same station (main and backup) share its logs.
- Spots are sold both ways: **booked into named breaks** ("Publicidade" between 12:00 and 14:00) and as **so many
  a day within hours**.
- Logs are **made automatically every night** (`radio-schedule make` from cron or Task Scheduler), and people can
  check and change them before they air.
- **No prices or invoicing**: invoices are made elsewhere, from the proof of broadcast.

## Conventions

As for the library, plus:

- **Station time.** Logs are made for a station's broadcast day in its time zone (`stations.time_zone`, an IANA
  name such as `Europe/Lisbon`; empty = the computer's own). Dates are `yyyy-MM-dd` and times of day
  `HH:mm:ss`, both local to the station.
- **Seconds** for lengths and offsets (`REAL` in SQLite, `DOUBLE` in MySQL).
- **Timing** values: `none`, `soft`, `hard`, `end_at`. **Lock** values: `free`, `movable`, `locked` (as in the
  playout: what the host may do with the item).

## Tables

### `stations`

| Column | Type | |
| --- | --- | --- |
| `id` | integer | |
| `name` | text, unique | |
| `time_zone` | text | IANA name; `''` = the computer's own |
| `active` | 0/1 | |

### `schedule_settings`

Per station, name and value (primary key `station_id`, `name`).

| Name | Default | |
| --- | --- | --- |
| `days_ahead` | `2` | how far ahead `radio-schedule make` keeps logs made |
| `song_rest_minutes` | `180` | not the same song again within this |
| `artist_rest_minutes` | `60` | not the same artist within this |
| `title_rest_minutes` | `120` | not the same title (another version) within this |

### `clocks` and `clock_slots`

A clock (`id`, `station_id`, `name`, `color`, `active`) is the plan of an hour; its slots are the parts, in
`position` order.

| Column | Type | |
| --- | --- | --- |
| `id`, `clock_id`, `position` | integer | |
| `kind` | text | `pick` (from a category by the rotation rules: songs, or a jingle from a jingle category), `item` (one track), `break` (commercial break), `event` (a file, a folder's newest file, or a stream), `voice` (later) |
| `category_id` | integer, null | `pick`: the category |
| `track_id` | integer, null | `item`: the track |
| `source` | text, null | `event`: file, folder or stream URL |
| `name` | text, null | shown in the playlist and reports; for breaks, what bookings name ("Publicidade") |
| `at_seconds` | real, null | when in the hour it is due (0–3599); null = after the slot before |
| `timing` | text | with `at_seconds`: `soft`, `hard` or `end_at` (`at_seconds` is when it **ends**) |
| `max_delay` | real, null | `soft`: at most this many seconds late |
| `lock_level` | text | what the host may do with what the slot puts in the log |
| `max_seconds` | real, null | `break`: longest it may run, jingles included; `event` stream: how long it plays |
| `intro_track_id`, `outro_track_id` | integer, null | `break`: opening and closing jingle |

### `clock_grid` and `clock_overrides`

Which clock plays each hour: `clock_grid` (`station_id`, `weekday` 0 = Sunday … 6, `hour` 0–23, `clock_id`) for
the week; `clock_overrides` (`station_id`, `date`, `hour`, `clock_id`) for particular dates such as holidays, and
it wins over the grid. An hour with no clock gets nothing in the log (the playout plays its playlist and fill
music then).

### `rotation_rules`

Per station and category (`station_id`, `category_id`, unique): `song_rest_minutes`, `artist_rest_minutes`,
`title_rest_minutes` (null = the station's default), `max_per_hour`, `tempo_flow` (1: not two slow songs, tempo
1–2, in a row) and `voice_flow` (1: not two songs of the same voice in a row).

### `advertisers`

Shared by the stations: `id`, `name` (as on the proof of broadcast), `competition` (a code such as `cars`:
advertisers with the same code, any case, are kept out of the same break), `contact`, `notes`, `active`.

### `campaigns`, `campaign_spots`, `bookings`

| Table | Columns |
| --- | --- |
| `campaigns` | `id`, `station_id`, `advertiser_id`, `name`, `starts_on`, `ends_on` (inclusive), `status` (`booked`, `paused`, `cancelled`), `notes`, `created_at`, `updated_at` |
| `campaign_spots` | the audio: `id`, `campaign_id`, `track_id`, `weight` (how often this version plays compared with the others), `starts_on`, `ends_on` (null = the whole campaign) |
| `bookings` | how it is placed: `id`, `campaign_id`, `days` (e.g. `12345` = Monday to Friday, `0` = Sunday), `per_day`, `from_time`, `to_time` (`24:00:00` = the end of the day), `break_name` (only breaks with this name), `position` (`any`, `first`, `last`), `fixed` (1: only in its hours; 0: it may go to a break up to an hour outside them when those are full) |

### `logs` and `log_items`

A log (`id`, `station_id`, `date` unique per station, `status`, `revision`, `made_by`, `checked_by`,
`checked_at`, `created_at`, `updated_at`) is a station's day. `status`: `draft` (being made or edited; the playout
doesn't load it), `ready`, `on_air`. `revision` goes up with every change, so the playout knows to reload what it
hasn't played.

| `log_items` column | Type | |
| --- | --- | --- |
| `id`, `log_id`, `hour`, `position` | integer | in order within each hour |
| `kind` | text | `song`, `jingle`, `spot`, `other`, `event`, `voice` |
| `track_id` | integer, null | |
| `source` | text, null | `event` |
| `title`, `artist` | text | as scheduled (for spots, `artist` is the advertiser) |
| `duration` | real | expected seconds, cue in to cue out |
| `starts_at` | time | when it is expected to start |
| `due_at` | time, null | first item of a timed break or event: when it is due (`soft`, `hard`) or has to end (`end_at`) |
| `timing`, `max_delay`, `lock_level` | | as in the clock slot |
| `group_key`, `group_name` | text, null | items that belong together (a break, an event) and their label |
| `clock_slot_id`, `campaign_spot_id`, `booking_id` | integer, null | where it came from |
| `state` | text | `scheduled`, `loaded` (in a studio's playlist), `aired`, `missed`, `removed` (by the host) |

### `plays` (new columns)

`station_id`, `log_item_id` (the log item it came from), `ended_at` (null if unknown) and `kind` (`music`,
`jingle`, `advert`, `programme`, `stream`), so the proof of broadcast and reconciliation can come from the
database. The playout fills them from step 2.

## How a log is made

`radio-schedule make` (or the scheduling program) makes, for each day from today to `days_ahead` that has no log:

1. **Each hour from its clock.** Slots in order, each expected to start where the one before ends; a timed slot
   where its time puts it (a soft one waits, a hard one is on time, an `end_at` one starts its length before its
   time).
2. **Picks by the rotation rules.** From the slot's category: not the same song, artist or title within their rest
   times (looking at what went on air and what this station's other logs plan, across midnight), not more than the
   category's hourly limit, and the tempo and voice flow if asked. Among the songs that pass, one of the
   longest-rested quarter, at random (repeatable: the same day gives the same choices). When nothing passes, the
   rules give way one at a time (flow, title, artist, song), and that is reported.
3. **Traffic.** Each booking of the day is placed, the most tied-down first (a named break, fixed; then the
   narrowest hours): spread evenly over its breaks, one spot per campaign and per advertiser in a break, never two
   advertisers with the same competition code together, `first` / `last` at most once a break, and a break no
   longer than its `max_seconds`. The campaign's versions take turns by weight, carrying on from day to day. What
   can't be placed is reported. A break with no adverts is left out.
4. **Gaps filled.** Where the hour would reach a timed break or event early, or end short of 60 minutes, extra songs
   (by the same rules) fill the gap; a song that fits is preferred, and a gap no song fits is left to the playout,
   which backtimes on air.
5. **Written** in one transaction, `ready`, revision 1, `made_by` = `auto`.

Logs that exist are never made again by `make`; their adverts are brought up to date (below). `remake` makes a day
again as long as none of it is in a studio's playlist or has gone on air.

### Late bookings and cancellations

`radio-schedule traffic` (and `make`, for the days it doesn't make) updates the adverts of logs already made:
spots of campaigns that were cancelled, paused or changed come out, and bookings that have fewer spots than they
should get them, in breaks of the clocks (also breaks that had no adverts before). Breaks starting within five
minutes, and anything already loaded or played, are left alone. Items that don't change keep their ids; the log's
`revision` goes up.

## `radio-schedule`

```
radio-schedule make                      make the logs from today to days_ahead, update the adverts of the others
radio-schedule make --days 7             … a week ahead
radio-schedule make --date 2026-12-24    one day
radio-schedule remake --date 2026-12-24  that day again (not once it is on air)
radio-schedule traffic                   only update the adverts
radio-schedule show [--date …]           print a day's log
radio-schedule check --date … --by Ana   mark a day as checked
radio-schedule stations                  list the stations
radio-schedule example                   an example clock for every hour (a station with no clocks)
  --station NAME|ID                      which station (default: the first)
  --db FILE | --mysql USER:PASSWORD@HOST[:PORT]/DATABASE   which library (default: the app's)
```

Run `make` every night (cron: `0 2 * * * radio-schedule make`), or every few minutes to take late bookings as soon
as they are entered. Making a day from an 80,000-song library takes a few seconds.

## How the playout will use a log (step 2)

- Each hour is loaded about an hour before it starts (`ready` logs only), after what is already in the playlist,
  and its items marked `loaded`. Without a log, the playout plays its playlist and fill music as now.
- Songs are ordinary playlist items: the host can change free ones. Breaks and events come in as groups with their
  timing and lock, and backtiming works as it does for the Schedule window's breaks.
- When `revision` changes, breaks not played yet are replaced in the playlist; songs the host changed stay.
- Every item that goes on air is written to `plays` with its `log_item_id`, and the item becomes `aired`; items
  whose hour ended without them become `missed`, and items the host removed `removed`.
- The Schedule window (`schedule.json`) stays for studios without a scheduling program.
