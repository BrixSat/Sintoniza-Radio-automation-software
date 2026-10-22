"""The user manual's text, in English (build.py puts it together). Blocks: ("h2", text) · ("h3", text) · ("p", html) ·
("steps", [html…]) · ("list", [html…]) · ("tip", html) · ("warn", html) · ("pre", text) · ("table", [header…], [[cell…]…]) ·
("icons", [(png, name, text)…]) · ("fig", shot, caption, [(control, explanation)…], [zoom]): a screenshot and the
controls to point at, in order; each gets a number on the picture and the same number in the list under it."""

# The manual's own words (cover, contents, notes).
WORDS = {
    "title": "Sintoniza",
    "subtitle": "User manual",
    "contents": "Contents",
    "tip": "Tip",
    "important": "Important",
    "version": "Version",
    "date": "%d %B %Y",
}

CHAPTERS = [
("Sintoniza at a glance", "intro", [
    ("p", "Sintoniza is radio automation for stations that are on air around the clock. It is made of four programs. Three "
          "share one database, the <b>library</b>: the music and its categories, the schedule, the daily playlists and the "
          "history of everything that went on air."),
    ("icons", [
        ("radio-playout.png", "Sintoniza Emi", "The on-air program (from <i>emissão</i>), in the studio: the playlist, the cartwall, the microphone and the stream. The host uses it."),
        ("radio-playout-schedule.png", "Sintoniza Pro", "The station's production program: its settings, categories and cartwalls; events, ad breaks, clients and campaigns, hour templates for your shows, and the daily playlists song by song."),
        ("radio-playout-import.png", "Sintoniza Import", "Brings folders of music into the library, in categories."),
        ("radio-playout-rec.png", "Sintoniza Rec", "Records what the station broadcasts and keeps it the days the law asks for; exports a stretch. It works on its own, without the library."),
    ]),
    ("p", "Each has its own entry in the app menu (Linux), the Start menu and desktop (Windows) or Applications (macOS), and "
          "only one copy of each runs on a computer. With a shared MySQL / MariaDB library, Pro and Import can run on "
          "other computers (the commercial department, a music office); the studio sees their changes within seconds."),
    ("tip", "The on-air program's <b>Settings</b> only has what belongs to that computer: sound cards, microphone, the "
            "database, keys. <b>Everything about the station</b> — name and logo, crossfades, fill music, time signal, streaming, "
            "RDS texts, website, alerts, categories, cartwalls — <b>is set once in Sintoniza Pro</b>, for every studio."),
    ("h2", "How the day is planned and played"),
    ("steps", [
        "<b>Import</b> the music into categories, under the station's eight (Music › Current, Gold, 80s, Jingles, Spots…).",
        "In <b>Pro</b>, make <b>hour templates</b> for your shows (which categories play, in which order) and place them on the week; book the <b>ad breaks</b> and their <b>clients</b>.",
        "The <b>daily playlists</b> are made from the templates for today and the next days. Look at them, change songs by hand, or let the rules fill them.",
        "The <b>on-air program</b> plays the daily playlists, starts the ad breaks and events at their times, and keeps every hour on time.",
    ]),
]),

("First setup", "setup", [
    ("p", "On <b>each studio computer</b>, in the on-air program: click <b>Settings</b> (bottom left, or Ctrl/Cmd + ,). "
          "Only what belongs to that computer is there. <b>Save</b> applies the changes. Once the library is set up, Settings "
          "asks for a user who may change this computer's settings (at first: <b>admin</b>, password <b>admin</b>)."),
    ("fig", "settings-station", "Settings → General", [("LanguageBox", "The <b>language</b> of the screens (English, Português, Français); it applies the next time the program starts.")]),
    ("fig", "settings-audio", "Settings → Audio", [
        ("MusicDeviceBox", "The sound card and channel pair for <b>Music</b>; Cartwall and Preview below it. <b>Test</b> plays a short tone."),
        ("PreviewDeviceBox", "<b>Preview</b> is for listening before air: headphones or the desk's PFL/cue, never an on-air output.")]),
    ("fig", "settings-library", "Settings → Library", [
        ("LibraryTypeBox", "Where the library lives: <i>SQLite</i> (a file on this computer) or <i>MySQL / MariaDB</i> (a server several studios share)."),
        ("TestLibraryButton", "<b>Test</b> opens it (and makes its tables the first time).")]),
    ("fig", "settings-safety", "Settings → Safety", [
        ("ResumeBox", "<b>Carry on playing when the app starts, if it is in AUTO</b>, <b>Start the app again if it crashes</b>, <b>Start the app when I log in</b>: for a station that is never off air."),
        ("BackupBox", "<b>Backups</b>: every day, of the database and the station's files.")]),
    ("p", "Then <b>once, for the whole station</b>, in <b>Sintoniza Pro → Station settings</b> (log in as <b>admin</b> / <b>admin</b>, and change that password in Station → Users):"),
    ("fig", "station-station", "Station settings → Station", [("StationNameBox", "<b>Station name</b>, <b>logo</b>, genre and website: shown next to the clock and sent to the stream and RDS. "
        "<b>Station type</b>: <i>FM radio</i> (microphone through the mixing desk) or <i>Web radio</i> (through the computer).")]),
    ("fig", "station-audio", "Station settings → Audio", [("CrossfadeBox", "Crossfade from song to song, in seconds."),
        ("OtherCrossfadeBox", "Crossfade into or out of jingles, adverts and programmes (0 = clean cut)."),
        ("LoudnessBox", "Every item at the same loudness (EBU R128).")]),
    ("fig", "station-safety", "Station settings → Safety", [
        ("SilenceBox", "<b>Detect silence</b>: an alarm (and, in AUTO, a skip) when nothing is heard."),
        ("FillBox", "<b>Fill music</b>: what plays if the playlist ever runs out, and the rest times (a song not again within N hours, an artist not within N minutes). The daily playlists use the same rest times.")]),
    ("p", "The other tabs there: <b>Time signal</b>, <b>Library</b> (other music folders), <b>Streaming</b>, <b>RDS</b> (the texts), "
          "<b>Website</b>, <b>Alerts</b> and <b>Playlist</b> (days made ahead, row colours). Every studio takes the station's "
          "settings within a minute of saving. The first studio started after updating to this version publishes its own."),
    ("tip", "Then import the music (chapter 5), make the cartwalls, build the templates and ad breaks (chapter 4), switch the on-air program to <b>AUTO</b> and press <b>Play</b>."),
]),

("The on-air program", "onair", [
    ("p", "<b>Sintoniza Emi</b> is what the host works with. The top half is what is on air now; the bottom "
          "half is what comes next (the playlist) and what is at hand (cartwall, library, folders)."),
    ("h2", "Logging in"),
    ("p", "The program opens <b>locked</b>: the on-air screen shows behind the log-in, but nothing can be clicked and the keys do nothing "
          "until a user with the <b>On air</b> permission logs in. Locked or not, the station stays on air: in <b>AUTO</b> it starts "
          "playing by itself when the program starts, the schedule and the ad breaks run, and the web remote works."),
    ("fig", "main-locked", "Locked: log in to work on air", [
        ("UserBox", "The user (the last one is filled in)…"),
        ("PasswordBox", "…and the password."),
        ("LoginButton", "<b>Log in</b> (or Enter)."),
        ("LockNote", "What is on air carries on meanwhile."),
    ]),
    ("tip", "<b>Log out</b> (bottom left) locks it again, e.g. between shows. While locked the window can't be closed. If the library can't be reached there are no users to check: the program opens without a log-in, so the station can still be run."),
    ("h2", "The screen"),
    ("fig", "main", "The on-air screen", [
        ("OnAirBadge", "State: <b>ON AIR</b>, <b>STOPPED</b>, <b>SILENCE</b> (nothing heard), or the countdown to the next ad break."),
        ("NowTitle", "What is on air, with its artist."),
        ("WaveMain", "The waveform: the red part has played. The thin bar below is the whole song."),
        ("Elapsed", "Time played, and the <b>NEXT</b> item below it."),
        ("NoteBox", "<b>Notes for the presenter</b> from the daily playlists: the item on air's and the next one's (what to announce, the sponsor to mention)."),
        ("PlayButton", "<b>Play</b> (F5)."),
        ("PauseButton", "<b>Pause</b> / resume (F6)."),
        ("NextButton", "<b>Next</b> (F7): crossfades to the next item now."),
        ("StopButton", "<b>Stop</b> (F8)."),
        ("Remaining", "Time left of the item on air."),
        ("AutoSwitch", "<b>AUTO</b> runs through the playlist; <b>ASSISTED</b> stops after each item until you start the next."),
        ("Clock", "The clock, the station name and logo."),
        ("TopOfHourText", "Countdown to the top of the hour."),
        ("PlaylistEndsText", "When the playlist runs out."),
        ("NextEventPanel", "<b>NEXT EVENT</b>: the next thing with a fixed time (an event, an ad break, a timed item) and a large countdown, amber in the last minute, red in the last 10 seconds."),
        ("MeterL", "Output levels, left and right."),
        ("Playlist", "The playlist (next pages)."),
        ("CartwallTab", "<b>CARTWALL</b>, <b>LIBRARY</b> and <b>FOLDERS</b> tabs."),
        ("Cartwall", "The cartwall: jingles and sounds at hand."),
        ("SettingsButton", "<b>Settings</b> (Ctrl/Cmd + ,)."),
        ("ReportsButton", "<b>Reports</b>: music played and proof of broadcast."),
        ("LogoutButton", "<b>Log out</b>: locks the screen again (on air carries on)."),
        ("Lights", "Status lights: <b>STREAM</b>, <b>RDS</b>, <b>REC</b> (Sintoniza Rec on this computer), <b>LIBRARY</b>, <b>ALERTS</b> — green working, red a problem (point at it for what), grey off."),
        ("LastLog", "The last thing that happened (all of it is in the log)."),
    ]),
    ("h2", "The playlist"),
    ("p", "Each row shows a type icon (♫ song, ✦ jingle, € advert, 🎙 programme, stream), the title and artist, the expected "
          "<b>start</b> time and the <b>length</b>. Everything that isn't a song has its own background colour (Settings → "
          "Playlist). The item on air fills with red as it plays, and a line marks where each hour starts."),
    ("fig", "main", "Playlist buttons", [
        ("OnAirButton", "<b>On air</b>: jump back to the item on air (it lights up when you scroll away)."),
        ("HourViewButton", "<b>This hour</b>: the hour before, this hour and the first 30 minutes of the next. Off: everything still to play."),
        ("AddStreamButton", "<b>Stream…</b> another station's stream, or an input of this computer live (the desk's line, a satellite receiver, a remote studio); next to it <b>Open…</b> and <b>Save…</b> (M3U playlists) and ▲ ▼ to move. Songs come from the Library on the right."),
        ("RemoveButton", "<b>Remove</b> the selected items."),
        ("ClearButton", "<b>Clear</b>: empties the playlist except the item on air and what the schedule locked; the plan refills it."),
    ], True),
    ("list", [
        "<b>Double-click</b> an item (or press Enter) to put it on air now.",
        "<b>Drag</b> rows to reorder them, or drop songs from the library, the folders tab or your file manager.",
        "<b>Right-click</b>: <i>Edit cue points…</i> (Ctrl/Cmd+E), <i>Edit tags…</i> (Ctrl/Cmd+T), <i>Remove</i>.",
        "Rows tagged <b>SCHEDULED</b> are ad breaks and events coming up, shown where they will play.",
    ]),
    ("warn", "<b>Adverts are protected.</b> While an advert is on air, Next, Pause, Stop and putting another item on air ask first. "
             "Only a click on the button in the question does it, so a paid spot isn't cut by accident."),
    ("p", "<b>Where the songs come from:</b> the daily playlists (chapter 4). About half an hour before each hour, its songs are "
          "put in the playlist. Near the end of the hour the program keeps time: if the next hour would start late, an "
          "<i>optional</i> item is left out; if it would start early, songs are added. You can still change the playlist on air."),
    ("h2", "Cartwall"),
    ("fig", "main", "The cartwall", [
        ("Cartwall", "The carts: name, icon and colour; the number is the cart's key (Settings → Shortcuts)."),
        ("CartwallPicker", "Which cartwall is shown."),
    ], True),
    ("list", [
        "<b>Click</b> a cart to play it, click again to stop. Carts play over the music on the Cartwall output. Each shows how long it lasts; while it plays, the time left.",
        "The cartwalls are the station's: they are made in Sintoniza Pro → <b>Cartwalls</b>, and every studio gets them within a minute.",
        "Keys for each cart: Settings → Shortcuts.",
    ]),
    ("h2", "Library"),
    ("fig", "main-library", "The Library tab (Ctrl/Cmd + F)", [
        ("SearchBox", "Search every song as you type: artist, title, album or file name (case and accents don't matter)."),
        ("CategoryBox", "Only one category (a category includes the ones under it)."),
        ("EditCategoriesButton", "Edit the categories."),
        ("TrackList", "The songs. <b>Double-click</b> or Enter: to the end of the playlist. <b>Right-click</b>: Play next, Category, Also in, Tempo, Voice, Language, Edit tags…, Edit cue points…"),
        ("ScanButton", "<b>Scan</b>: bring the library up to date with the music folders now."),
    ]),
    ("fig", "main-folders", "The Folders tab", [("Library", "The music folders on disk: drag files or folders to the playlist. Right-click a folder → <i>Category</i> puts all its songs (and songs put there later) in a category.")]),
    ("h2", "Cue points"),
    ("fig", "cue-editor", "Edit cue points…", []),
    ("p", "Set <b>Cue In</b> (where it starts), <b>Intro</b> (end of the intro, for talking over it), <b>Outro</b>, <b>Mix</b> "
          "(where the next item starts) and <b>Cue Out</b>. <i>Auto-detect</i> finds the first and last sound and a good Mix "
          "point. Space plays and pauses on the Preview output; ← → move a second (Shift: 0.1 s); the mouse wheel zooms."),
    ("h2", "Segues"),
    ("p", "How one song goes into the next: right-click a playlist item → <i>Segue with the next…</i> (Ctrl/Cmd+G), or <b>Segue…</b> in Daily playlists."),
    ("fig", "segue", "Segue with the next…", [
        ("Join", "The end of the first song (top) and the start of the next (bottom), joined at 0. <b>Drag</b> either song to move the join; the yellow line is the first one's level (its fade)."),
        ("MixText", "<b>Mix</b>: where the next song starts. Moving it makes a Mix marker: the first song then plays on at full level to its Cue Out. <i>Automatic</i> goes back to the usual crossfade."),
        ("OutText", "<b>Cue Out</b>: where the first song stops."),
        ("InText", "<b>Cue In</b>: where the next song starts in its file (not past its Intro)."),
        ("PlayButton", "<b>Play the join</b> from a few seconds before, exactly as on air, on the Preview output (Space). Change something while it plays and you hear it again at once."),
        ("SummaryText", "What will happen, in words."),
    ]),
    ("p", "Saved as the two songs' cue points: they apply wherever the songs play, in every studio sharing the library."),
    ("h2", "Tags"),
    ("fig", "tag-editor", "Edit tags…", []),
    ("p", "Title, artist, album, genre, year, track number, comment and cover picture, written in the file. A file that is on air can't be changed until it ends."),
    ("h2", "Reports"),
    ("fig", "reports-adverts", "Reports", [
        ("KindBox", "<b>Music played</b> (royalties), <b>Music totals per song</b>, <b>Adverts</b> (proof of broadcast) or <b>Everything</b> (as-run log)."),
        ("AdvertiserBox", "For the proof of broadcast: one advertiser or all."),
        ("HtmlButton", "<b>Save as a page to print…</b> (or as PDF); next to it, <b>Save as CSV…</b> for a spreadsheet."),
    ]),
    ("h2", "GPIO: the desk's faders and lights"),
    ("p", "Settings → <b>GPIO</b> uses a serial port's control lines (a USB serial adapter works). Inputs <b>CTS</b> (pin 8), <b>DSR</b> (6) and "
          "<b>DCD</b> (1) — a contact between the line and DTR (4) or RTS (7) — each do something when the contact <b>closes</b> and when it <b>opens</b>: "
          "Play (fader start), Next, Stop, Pause / carry on, a cart of the cartwall on screen, or nothing. Outputs <b>DTR</b> and <b>RTS</b> are on while "
          "something plays, an advert is on air, a live input or stream is on air, or there is silence (one not used is held on, as the supply of the inputs)."),
    ("tip", "The usual fader start: CTS closes → Play; opens (fader down) → Next or nothing. A contact that bounces counts once; Next, Stop and Pause "
            "aren't done while an advert is on air. <b>Test the inputs</b> shows them as you move the faders."),
    ("p", "<b>Network</b>: a GPIO-to-network box or a desk that sends texts, on a port (TCP or UDP). Each <b>text</b> does something (FADER1_ON → Play), "
          "and texts are <b>sent</b> when a signal starts and ends (ONAIR_ON / ONAIR_OFF), to every TCP connection and, if set, by UDP. <b>Only from</b>: "
          "the addresses allowed — anyone who can reach the port can press Play."),
    ("p", "<b>MIDI</b> (Linux and Windows): a digital mixer or a controller. Each <b>note</b> or <b>controller</b> (channel and number) does something "
          "when <b>pressed</b> and when <b>released</b> (a fader: from the middle up, once each way); <b>lights</b> are notes sent on and off. For both, "
          "<b>Listen</b> shows what arrives and <b>Add the last received</b> puts it in the list. Serial, network and MIDI can be used together."),
    ("h2", "Web remote"),
    ("p", "Control the playout from a phone or another computer on the station's network: a web page, no app to install."),
    ("fig", "settings-remote", "Settings → Remote", [
        ("RemoteBox", "Switch it on (it is off until you do)."),
        ("RemotePortBox", "The port (8090 by default). The page asks for a station user with the <b>Web remote</b> permission, e.g. <b>dj</b> (chapter 4, Users)."),
        ("RemoteAddressText", "The address to open on the phone, on the same network."),
    ]),
    ("p", "The page is laid out as the on-air screen: the deck with the waveform, the station panel with the clock and the level meters, the playlist, and the cartwall and library."),
    ("fig", "remote-desktop", "The web remote on a computer", []),
    ("list", [
        "What is on air with its waveform, the time played and left, the talk-over and ad break countdowns, the stream and silence; the clock, top of hour, when the playlist ends, the next event and the levels.",
        "<b>▶ ❚❚ ⏭ ■</b> and <b>AUTO</b>, as on screen. An advert on air is only cut after a question.",
        "<b>The playlist</b>, with its colours and tags: tap an item for <i>▶ Now</i> (puts it on air) and <b>✕</b> (removes it).",
        "<b>CARTWALL</b>: the carts fire with a tap; the list next to the tabs picks another cartwall. <b>LIBRARY</b>: search the library and add a song after the item on air.",
    ]),
    ("fig", "remote-phone", "The web remote on a phone: the same, one panel under the other", []),
    ("warn", "It is plain HTTP, for the station's own network. From outside, use it only through the station's VPN — never open its port on the router. Five wrong passwords lock the page for a minute."),
    ("h2", "The microphone (web radio)"),
    ("p", "With <i>Web radio</i> chosen (Pro → Station settings), the <b>MIC</b> button (F9) opens the microphone and the music and carts fade down while it is open: hold to talk, or press on and press off (Settings → Microphone). With <i>FM radio</i> the microphone goes through the desk."),
    ("h2", "All the settings"),
    ("p", "This computer's, in the on-air program's <b>Settings</b>:"),
    ("table", ["Tab", "What it is for"], [
        ["General", "The language of the screens"],
        ["Audio", "Outputs for Music / Cartwall / Preview"],
        ["Microphone", "(web radio) input, levels, how far the music goes down"],
        ["Safety", "Start-up and crash behaviour, the log, backups"],
        ["Library", "The database, automatic scans"],
        ["Shortcuts", "Keys for Play, Pause, Next, Stop, MIC, the stream and each cart"],
        ["RDS", "Whether this computer sends to the RDS encoder, and how it reaches it"],
        ["Playlist", "How much of the next hour the playlist shows"],
        ["Remote", "The web remote: on/off, port, its address (it logs in with the station's users)"],
        ["About", "Version, updates and this manual"],
    ]),
    ("p", "The station's, in <b>Sintoniza Pro → Station settings</b>:"),
    ("table", ["Tab", "What it is for"], [
        ["Station", "Name, logo, genre, website, FM or web radio, money sign"],
        ["Audio", "Crossfades, loudness levelling"],
        ["Time signal", "Pips or a file at the top of the hour; hour announcements (HRS05.mp3 at 05:00…)"],
        ["Takes", "The takes' folder (the time, the temperature) and where the station is"],
        ["Safety", "Silence detection, fill music"],
        ["Library", "Other music folders (songs without a category)"],
        ["Streaming", "Icecast / Shoutcast server, format and bitrate"],
        ["RDS", "PS, radio text, RT+"],
        ["Website", "Now playing posted to the station's site"],
        ["Alerts", "Telegram, e-mail or web alerts: silence, stream down, crash, low disk, clock, backups"],
        ["Playlist", "How many days of daily playlists are made ahead, row colours"],
    ]),
]),

("Sintoniza Pro", "schedule", [
    ("p", "The station's production program: its settings, library, categories, cartwalls, schedule and advertising. It opens on <b>Home</b>, with the station at a glance."),
    ("fig", "production", "Sintoniza Pro: Home", [
        ("MainMenu", "The <b>menu</b>: File, Library, Programming, Traffic, Station, Help."),
        ("SaveButton", "The <b>toolbar</b>: Save (Ctrl+S) and the tools used most."),
        ("NavPanel", "The <b>tools</b>: Home; Library (songs, categories, import); Programming (hour templates, daily playlists, events); Traffic (ad breaks, clients and campaigns); Station (settings, cartwalls, users)."),
        ("HomeClock", "The clock and the date."),
        ("HomeToday", "<b>Today</b>: the hours planned, ad breaks and adverts booked, events, voice tracks."),
        ("HomeWarnings", "<b>Needs attention</b>: missing files, songs without a category, hours without a template, campaigns ending this week, unsaved changes."),
        ("HomeComputer", "<b>This computer</b>: system, processor, memory, free disk, how long it has been on. Next to it, the library and the database."),
        ("NowTitleText", "<b>Now playing</b> on air (from every studio's play history), and this hour's template."),
        ("BoxStationText", "The station, the database and who is working here."),
        ("StatusVersion", "The <b>status bar</b>: version, station, database, user, unsaved changes…"),
        ("StatusClock", "…and the date and time."),
    ]),
    ("h2", "Categories"),
    ("p", "Every station has the same eight categories at the top, which can't be renamed, moved or deleted: <b>Music</b>, "
          "<b>Jingles</b>, <b>Promos</b>, <b>ProgAutor</b> (shows recorded outside), <b>Fillers</b>, <b>Redação</b> (news), "
          "<b>Spots</b> (adverts) and <b>Takes</b> (voice inserts: the time, the temperature… as if a host were there). "
          "Your own go under them."),
    ("fig", "categories-fixed", "Categories: one of the station's eight", [
        ("CategoryList", "The categories, as a tree; 🔒 = one of the station's eight."),
        ("FixedPanel", "What it is for; only its colour and folders change."),
        ("AddChildButton", "<b>New under it</b>: a category of your own under the selected one (Music › 80s, Spots › Clients…)."),
    ]),
    ("fig", "categories", "Categories: a category of your own", [
        ("ParentBox", "<b>Under</b>: move it inside another category."),
        ("Swatches", "Its colour in the lists."),
        ("FolderList", "Its <b>folders</b>: their songs are in it, and new songs put there later too."),
        ("ApplyButton", "<b>Save changes</b>."),
    ]),
    ("h2", "Cartwalls"),
    ("fig", "cartwalls", "Cartwalls", [
        ("PageList", "The cartwalls: <b>New</b>, <b>Copy</b>, <b>Rename…</b>, <b>Delete</b>."),
        ("CartGrid", "Its 12 carts, as the studio sees them: click one to change it, or drop a file on it."),
        ("ChooseFileButton", "The cart's sound: <b>Choose file…</b>, <b>Listen</b> (Preview output), <b>Clear</b>."),
        ("ColorPanel", "Its colour…"),
        ("IconPanel", "…and icon."),
        ("SaveButton", "<b>Save</b>: every studio gets them within a minute."),
    ]),
    ("tip", "Use files every studio can reach (a shared folder), as for the music."),
    ("h2", "Users and permissions"),
    ("p", "The on-air program, Sintoniza Pro, Sintoniza Import, the on-air program's Settings and the web remote ask for a <b>user and password</b>: "
          "the station's users, the same in every studio. A new library has two: <b>admin</b> / <b>admin</b> (may do everything) "
          "and <b>dj</b> / <b>dj</b> (a presenter: on air, voice tracks, web remote). <b>Change those passwords first</b>: until then Pro's Home reminds you."),
    ("fig", "login", "Logging in", [
        ("UserBox", "The user (it remembers the last one)…"),
        ("PasswordBox", "…and the password."),
        ("LoginButton", "<b>Log in</b>. In Pro, File → <b>Switch user…</b> changes it and File → <b>Log out</b> covers Pro with the log-in again."),
    ]),
    ("fig", "users", "Station → Users", [
        ("UserList", "The station's users."),
        ("AddButton", "<b>New user</b>; <b>Delete</b> next to it."),
        ("NameBox", "The name…"),
        ("PasswordBox", "…and a new password (left empty, it stays as it is)."),
        ("EnabledBox", "<b>May log in</b>: untick it to stop someone without deleting them."),
        ("AdminPresetButton", "<b>Everything</b> or <b>Presenter</b> (on air, voice tracks and web remote) tick the usual sets."),
        ("PermissionPanel", "What they may use: Pro shows them only those tools, menus and buttons."),
        ("SaveUserButton", "<b>Save user</b>."),
    ]),
    ("table", ["Permission", "Lets them"], [
        ["On air", "The on-air program in the studio: playing, the playlist, the cartwall, the microphone"],
        ["Library", "Songs, categories, importing music (and Sintoniza Import)"],
        ["Programming", "Hour templates, daily playlists, events"],
        ["Voice tracks", "Record voice tracks: they see the daily playlists but can't change the songs"],
        ["Traffic", "Ad breaks, clients and campaigns"],
        ["Station", "The station's settings and cartwalls"],
        ["Users", "Add and change users"],
        ["This computer's settings", "Open a playout's own Settings (sound cards, database, keys…)"],
        ["Web remote", "Control a playout from a phone"],
    ]),
    ("tip", "Passwords are kept only as a secure hash: a forgotten one is set again by another user with <b>Users</b>. Nobody can delete or lock out themselves, nor take away their own <b>Users</b>."),
    ("h2", "ERC quotas (Portuguese music)"),
    ("p", "The Lei da Rádio (art. 41.º–47.º, as changed in 2024) asks, of the songs played in a month: at least <b>30 %</b> Portuguese music; of the "
          "Portuguese music, at least <b>60 %</b> in Portuguese performed by EU citizens and at least <b>35 %</b> first edited in the last 12 months — over "
          "the whole month and also <b>Monday to Friday, 7:00–20:00</b>. Every month the station sends the ERC what it played, through its portal."),
    ("p", "Each song needs: Portuguese music or not, the language it is sung in, the performer's country and its first edition. Give them to a whole "
          "category in <b>Categories</b> (its songs and the categories under it take them), or to songs in <b>Songs</b> → right-click → <b>ERC classification…</b>."),
    ("fig", "erc-song", "ERC classification of a song", [
        ("PortugueseBox", "<b>Portuguese music</b>: yes, no, or as its category says."),
        ("LanguageBox", "The <b>language</b> it is sung in (pt-PT, pt-BR, en…)."),
        ("CountryBox", "The <b>performer's country</b>, from the station's list with the ERC's codes."),
        ("ReleaseBox", "The <b>first edition</b>: yyyy-mm-dd, yyyy-mm or yyyy (empty: the year tag)."),
    ]),
    ("fig", "erc", "Programming → ERC quotas", [
        ("FromBox", "The period: <b>Last month</b>, <b>This month</b> or any dates…"),
        ("CheckButton", "…and <b>Check</b>."),
        ("AllPanel", "The three quotas over the whole period: green when met, red when not, with how many of how many."),
        ("DaytimePanel", "The same, Monday to Friday from 7:00 to 20:00. While the period isn't over, a third card adds the daily playlists still to play."),
        ("MissingList", "<b>Songs to classify</b>: played (or planned) without the ERC's details; until then they count as not Portuguese."),
        ("ClassifyButton", "<b>Classify…</b> the selected ones, or <b>🔎 Look up on MusicBrainz…</b>: their performer's country, language and first edition from MusicBrainz (internet), with what that suggests; you untick what isn't right and save the rest."),
        ("AddToLibraryButton", "Songs played from <b>files outside the library</b>: <b>Add to the library, in</b> a category, so they can be classified."),
        ("ExportButton", "<b>Export for the ERC (XML)…</b>: the file the ERC's portal takes, a PLAYLIST a day."),
        ("AliasBox", "The programme service's name at the ERC (ALIAS)."),
        ("CountryPanel", "The countries and their ERC codes (Portugal is 045; copy the others from the portal), and which are in the EU."),
    ]),
    ("tip", "The daily playlists can only plan to the quotas with <b>classified songs</b> to choose from: in <b>Songs</b>, select a category's songs → right-click → <b>ERC: look up on MusicBrainz…</b> (about 3 seconds a song)."),
    ("p", "<b>Planning to the quotas</b> (on the right, on by default): each hour of the daily playlists is made to meet the three quotas on its own — a "
          "Portuguese song about every third song when there is too little, and among them songs in Portuguese by EU performers and recent ones where those "
          "fall short — from the classified songs of each line's category, as the rest rules allow."),
    ("h2", "Song restrictions"),
    ("p", "<b>Songs</b> → right-click → <b>Restrictions…</b>: when a song may be picked automatically. The daily playlists always keep them; fill "
          "music keeps the dates, days and hours. Put in by hand, the song plays all the same. Restricted songs show ⏱ in the list."),
    ("fig", "restrictions", "Restrictions of a song", [
        ("FromBox", "<b>Only from</b>…"),
        ("UntilBox", "…<b>until</b> (empty: no limit) — a Christmas song in December."),
        ("DayGrid", "On these <b>days</b>…"),
        ("HourGrid", "…at these <b>hours</b> (lit = may play)."),
        ("DayHoursButton", "Quick choices: all, 7:00–20:00, 20:00–7:00."),
        ("MaxDayBox", "At most so many times a <b>day</b>…"),
        ("MaxWeekBox", "…and a <b>week</b>, Monday to Sunday (0: no limit)."),
        ("RemoveButton", "<b>No restrictions</b>."),
    ]),
    ("h2", "Takes: the time and the temperature"),
    ("p", "Short recorded inserts the playout puts together <b>when they go on air</b>, so they are always right. Put them in an hour template: "
          "<b>Add a line → A take ▾</b> → the time, the temperature, or both."),
    ("fig", "station-takes", "Station settings → Takes", [
        ("TakesFolderText", "The <b>folder</b> with the takes: HRS00–HRS23 (the hour), MIN01–MIN59 (the minutes; or HRS10_15 for a whole time), TMP0–TMP45 and TMPM1… below zero."),
        ("TakesFolderNote", "What it found there."),
        ("LatitudeBox", "Where the station is, for the temperature (Open-Meteo, free, every 10 minutes)…"),
        ("TemperatureButton", "…and <b>Read the temperature</b> to try it."),
    ]),
    ("tip", "Several recordings of the same take (HRS10a, HRS10b) are used at random. The time said is the nearest minute when it starts. A take whose files are missing is skipped."),
    ("h2", "The schedule"),
    ("p", "Everything planned ahead: Events, Ad breaks, Clients and campaigns, Hour templates and Daily playlists, chosen on the left. <b>Save</b> (in the toolbar, lit when something changed, or Ctrl+S) writes it to the database and the on-air program "
          "takes it within a few seconds. If someone else saved meanwhile, you are asked before theirs is replaced."),
    ("tip", "<b>The week calendars</b> (Events, Ad breaks, Playlists): <b>click a cell</b> to place the selected item at that "
            "day and time (again to take it out), <b>drag</b> to do many cells, <b>click a time</b> for every day. "
            "<b>Add row</b> adds an exact time, e.g. <code>12:59:55</code>; <code>*:59:55</code> is that minute of every hour."),
    ("h2", "Events"),
    ("fig", "schedule-events", "Events: things that play at set times", [
        ("EventList", "The events. <b>New</b> / <b>Delete</b> below."),
        ("EventGrid", "The week: where the selected event plays."),
        ("EventAddTimeBox", "An exact time to add as a row."),
        ("SaveButton", "<b>Save</b> the whole schedule."),
    ]),
    ("p", "An event plays a <b>file</b>, the <b>newest file in a folder</b> (the news: skipped if older than N minutes) or an "
          "internet <b>stream</b> or an <b>input of the studio computer</b> (live: <b>Input…</b>) for N minutes. How it starts: <b>Soft</b> (after the item on air ends, at most N minutes "
          "late), <b>Hard</b> (on the second; what is on air fades out) or <b>Ends at its time</b> (it ends exactly then, never "
          "later: the songs before it are adjusted, and what is on air fades if it would run over; one that ends at 10:00 is the last of 09:00; with the time signal on Stop, it ends as the pips start, and the hour before's songs don't play after them; a short gap before it is filled from the Fillers category). And what the host may do with it in the playlist: remove and move, only move, or nothing."),
    ("h2", "Ad breaks"),
    ("fig", "schedule-breaks", "Ad breaks", [
        ("BreakList", "The breaks."),
        ("BreakGrid", "Where they are in the week (here every hour at :50)."),
        ("PartList", "The <b>running order</b>: jingles and <b>sections</b>; the adverts booked in a section play there."),
    ]),
    ("p", "A jingle can be marked <i>play only when the section after it has adverts</i> (a section opener). A break with nothing booked is skipped. Breaks are locked in the playlist by default."),
    ("h2", "Clients and campaigns"),
    ("fig", "schedule-clients", "Clients", [
        ("ClientList", "The <b>clients</b> (the name on the proof of broadcast)."),
        ("CampaignList", "Their <b>campaigns</b>: the adverts (several take turns), section, position, first and last day."),
        ("SpotGrid", "The week's ad breaks: how many adverts and how long each runs. <b>Click</b> a break to book the campaign there, every week or on that date only."),
    ]),
    ("p", "<b>In every break</b> books a campaign in every break that has its section. <b>Position</b>: automatic, 1–5 from the start of the section, or 6–10 at its end."),
    ("p", "<b>Certificate…</b> (under the clients): what was booked for the client against what went on air, spot by spot, for a period and one campaign or all — "
          "<b>On air</b> with its time, <b>NOT ON AIR</b>, <b>On air, not booked</b>, <b>To come</b>. <b>Certificate to print…</b> gives the page to print or save as PDF "
          "for the client. With Sintoniza Rec recording, <b>▶ Listen</b> plays the selected advert as it went out and <b>Save its audio…</b> saves it as an MP3. "
          "Home warns when yesterday's booked adverts didn't go on air."),
    ("h2", "Playlists: hour templates for your shows"),
    ("fig", "schedule-templates", "Hour templates", [
        ("TemplateList", "The templates: one per show or kind of hour."),
        ("TemplateGrid", "The hours each plays: here the Morning show on weekdays 07–10."),
        ("LineList", "Its <b>lines</b>, played in order and then from the top again until the hour is full."),
        ("LineCategoryBox", "A line is a song from a <b>library category</b> (Music › Current, Gold, Jingles…)…"),
        ("TempoMinBox", "The selected line's songs: a <b>tempo</b> and <b>energy</b> range (here the first song of the morning is lively)."),
        ("AddFolderLineButton", "…or a file from a <b>folder</b>."),
    ]),
    ("h2", "Music rules"),
    ("p", "Give the songs a <b>tempo</b> (1 slow … 5 fast) and an <b>energy</b> (1 calm … 5 lively) in the library: select them, right-click → <i>Tempo</i> or <i>Energy</i>. Then a template's lines can ask for them, and the template keeps artists and albums apart."),
    ("fig", "schedule-rules", "A template's music rules", [
        ("TempoMinBox", "<b>Tempo</b> from … to … for the selected line (<i>any</i> = no limit)."),
        ("EnergyMinBox", "<b>Energy</b> from … to …."),
        ("ArtistHoursBox", "The same <b>artist</b> not again within these hours (0 = the rest time in Settings → Safety)."),
        ("AlbumHoursBox", "The same <b>album</b> not again within these hours (0 = no album rule)."),
        ("MaxSlowBox", "At most this many <b>slow songs</b> (tempo 1–2) in a row (0 = no limit)."),
    ]),
    ("tip", "A rule that would leave nothing to play is let go for that song (album first, then slow songs, then artist), so the hour is always full. A line whose tempo or energy no song has plays from its whole category, and Daily playlists says so."),
    ("h2", "Daily playlists"),
    ("p", "The hours as they will play, made from the templates: every song, jingle and advert, in order, with its start time."),
    ("fig", "schedule-daily", "Daily playlists", [
        ("DayBox", "The day: ◀ ▶ and <b>Today</b>."),
        ("HourList", "Its 24 hours, with the template and length of each. ✎ = changed by hand; ↻ = its template changed since."),
        ("OnAirText", "What is on air now, and how far ahead or behind the plan."),
        ("ItemList", "The hour: songs, jingles, the ad break's adverts (with the client) and events, each with its start time and where it came from. ✓ = played; ▶ = on air; OPTIONAL = only if there is time."),
        ("SegueButton", "<b>Segue…</b>: how the selected song goes into the next (see Segues)."),
        ("StreamButton", "<b>Stream…</b>: another station's stream at a time in this hour, on the dot or after the item on air. Remove takes it out."),
        ("TagsButton", "<b>Edit tags…</b> of the selected song."),
        ("ReplaceButton", "<b>Another song</b> from the same template line; ▲ ▼ move, <b>Remove</b> takes it out."),
        ("NoteButton", "<b>📝 Note…</b> on the selected item and <b>📝 Hour note…</b>: notes for the presenter, shown on air before and while it plays (📝 in the list)."),
        ("GenerateHourButton", "<b>Make this hour again</b> from its template."),
        ("GenerateDayButton", "<b>Make the day again</b> (hours changed by hand are kept)."),
        ("Tracks", "The library: <b>double-click</b> a song to put it after the selected item; right-click → Listen."),
        ("ListenButton", "<b>Listen</b> to the selected item (or double-click it, or Space), on the Preview output only."),
        ("TotalText", "The hour's total: songs and jingles, ad breaks and events, and what is optional."),
    ]),
    ("list", [
        "<b>Build it song by song</b> with Another song, ▲ ▼, Remove and the library. Each change is saved at once and the hour is marked ✎; it is never made again by itself.",
        "<b>Or let the rules do it</b>: today and the next days are always made, and an hour whose template changes is made again within minutes (unless it was changed by hand or is about to play).",
        "The rules: the songs rested longest first; a song not again within N hours, an artist not within N minutes (Station settings → Safety); never the same artist twice in a row while another can play.",
        "Songs the on-air program adds to keep time appear as <i>added on air to keep time</i>.",
    ]),
    ("h2", "Voice tracks"),
    ("p", "Record your talk between two songs ahead of time, as if you were live: select a song in an hour and click <b>Voice track…</b>."),
    ("fig", "voicetrack", "Voice track…", [
        ("InputBox", "The <b>input</b> to record from (the studio mic, or any input of this computer)."),
        ("PreRollBox", "How much of the song's end you hear first."),
        ("StartButton", "<b>Start</b> plays the end of the song before, on the Preview output."),
        ("TalkButton", "<b>Talk</b> (Space) starts recording — over the song's ending if you like; again to stop."),
        ("NextButton", "<b>Next song</b> (Enter) brings in the following item while you talk, under your voice."),
        ("OverlapText", "After the take: how far it starts over the song's end…"),
        ("NextText", "…and where the next song comes in. ±0.5 s to nudge them."),
        ("PlayBackButton", "<b>Play it back</b> as on air; <b>Record again</b> if you like."),
        ("SaveButton", "<b>Save</b>: kept as an MP3 in the station's voice tracks folder and in the library under <b>Takes</b>."),
    ]),
    ("fig", "schedule-voice", "The voice track in the hour", [
        ("ItemList", "It goes right after its song (🗣), starting over the song's last seconds."),
        ("VoiceButton", "<b>Voice track…</b> again on the song, or on the voice track, to record it again."),
    ]),
    ("tip", "A voice track belongs to the song before it: removing or replacing that song takes the voice track too, moving the song moves it, and on air it is skipped if the song can't play."),
]),

("Sintoniza Import", "import", [
    ("p", "Brings a folder of music into the library, with the folders inside it as categories. It asks for a user with the <b>Library</b> permission."),
    ("fig", "import", "Sintoniza Import", [
        ("FolderBox", "The <b>folder</b> to import (<b>Choose…</b> next to it)."),
        ("NameBox", "The <b>category</b> it goes into (by default the folder's name)…"),
        ("ParentBox", "…<b>under</b> one of the station's eight (Music by default) or a category under them."),
        ("KindBox", "The <b>kind</b> of new categories: it follows the one they go under."),
        ("LevelsBox", "<b>Folders inside become sub-categories</b>: none, or 1–3 levels (Music/80s → Music › 80s)."),
        ("AlsoInRadio", "Songs already in another category: keep it and add this one too, move them, or leave them."),
        ("PreviewButton", "<b>Preview import</b>: what it will do. Nothing is changed yet."),
        ("PlanList", "The categories it will make (NEW or ALREADY THERE), the files in each, how many are in the library already and how many are in another category."),
        ("ImportButton", "<b>Import</b>: makes the categories, reads the songs and puts each in its category."),
    ]),
    ("p", "Each folder is kept for its category: songs put there later get it too (the on-air program scans those folders by "
          "itself). <b>Stop</b> halts an import; importing again finishes it."),
    ("h2", "radio-import (command line)"),
    ("p", "The same scan without a window, e.g. every night from cron or Task Scheduler:"),
    ("pre", "radio-import                          scan the library's folders\n"
            "radio-import /music /jingles          scan these folders\n"
            "radio-import --db /path/library.db    an SQLite library file\n"
            "radio-import --mysql user:password@server[:port]/database\n"
            "radio-import --stats                  what is in the library"),
    ("p", "Only new and changed files are read. Deleted files, and the songs of a deleted folder, are taken out; a drive or share that isn't connected is left alone."),
]),

("Sintoniza Rec", "rec", [
    ("p", "The station's recorder: it records what goes out, all day, every day, and keeps it. The Lei da Rádio (art. 39.º) "
          "asks for the broadcasts to be recorded and kept at least 30 days. It doesn't need the library or a user, so it can "
          "run on the studio computer or on any other; on a computer of its own it keeps recording when the studio's is restarted."),
    ("fig", "rec", "Sintoniza Rec", [
        ("RecText", "<b>● REC</b> while it records (STOPPED in red when it doesn't, SILENCE when there has been no sound for a while), and for how long."),
        ("SourceText", "What it records from, and how."),
        ("MeterL", "The level of what comes in."),
        ("DiskText", "The file being written, the free space, how long recordings are kept and how often a new file starts."),
        ("RecButton", "<b>Record</b> / <b>Stop</b>. Closing the window asks first: it stops the recording."),
        ("MonitorButton", "<b>Listen live</b>: what is being recorded, on the Listening output (headphones, never an output that goes on air)."),
        ("SettingsButton", "<b>Settings</b>: the source, the files, listening and alerts."),
        ("DayBox", "The <b>recordings of a day</b> (Today goes back to today)…"),
        ("FileList", "…each file with its start and end, length and size. Double-click one, or pick it and…"),
        ("PlayButton", "…<b>▶</b> to listen to it; the bar under it moves through it, with the time of day it was on air."),
        ("FromBox", "<b>Export a stretch</b>: a day, a start and an end time and a format…"),
        ("ExportButton", "…<b>Export…</b> asks where to save one file, cut to the second, taken from as many recordings as needed. A time the recorder was stopped is left out, and it says so."),
    ]),
    ("h2", "Where it records from"),
    ("fig", "rec-settings", "Settings → Source", [
        ("StationBox", "The station's name, in the window and in the alerts."),
        ("InputRadio", "An <b>input</b> of this computer…"),
        ("InputBox", "…which one: best, the desk's programme output into the sound card, so the microphones are in it (the playout's own sound has no microphones)."),
        ("StreamRadio", "Or the station's <b>stream</b>…"),
        ("StreamBox", "…its address. If the stream drops it is opened again by itself, in a new file."),
        ("AutostartBox", "<b>Start Sintoniza Rec when I log in</b>: after a power cut or a restart it records again by itself."),
    ]),
    ("h2", "The files"),
    ("fig", "rec-files", "Settings → Files", [
        ("FolderBox", "The <b>folder</b>: inside it, a folder a day (2026-10-21) with the files (RE_2026-10-21_10-00-00.mp3). A shared folder works too."),
        ("FormatBox", "MP3, or WAV (no compression: about 10 GB a day)."),
        ("BitrateBox", "The MP3 quality; <b>Mono</b> takes half the space."),
        ("MinutesBox", "<b>A new file every</b> 5 to 60 minutes, from a set minute. The new file starts before the old one stops: nothing is lost between them."),
        ("KeepDaysBox", "<b>Kept for</b>: older recordings are deleted by themselves. 30 days at least."),
        ("SpaceText", "How much space a day and the whole period take."),
        ("ArchiveBox", "An <b>archive copy</b> as well: mono and low quality, in another folder (a NAS, another disk), kept for months or always."),
    ]),
    ("h2", "Alerts"),
    ("fig", "rec-alerts", "Settings → Alerts", [
        ("SilenceDbBox", "<b>Silence</b>: below this level for this long (or a stream that sends nothing). It says again when the sound is back."),
        ("AliveBox", "A <b>still recording</b> message once a day: a day without one means something is wrong with the computer."),
        ("TelegramTokenBox", "Sent by <b>Telegram</b>…"),
        ("SmtpServerBox", "…by <b>e-mail</b>…"),
        ("WebhookBox", "…or to a <b>web address</b> (JSON). Whatever is filled in is used."),
        ("TestAlertButton", "<b>Send a test</b>."),
    ]),
    ("p", "Also sent: a recording that can't be written (a full or missing disk, a share that went away: it tries again with a new file) and less than 2 GB free."),
]),

("How do I…", "howto", [
    ("h3", "…give a presenter their own login?"),
    ("steps", ["Pro → Station → <b>Users</b> → <b>New user</b>.", "Their name and a password; click <b>Presenter</b>.",
               "<b>Save user</b>. They can work on air, record voice tracks in Pro and use the web remote, nothing else."]),
    ("h3", "…set up a weekday morning show?"),
    ("steps", ["Pro → <b>Playlists</b> → <b>New</b>; name it.", "Add its lines: e.g. Music › Current, Gold, Jingles, Current, 80s.",
               "Drag over the hours 07–10, Monday to Friday. <b>Save</b>.", "Check it on <b>Daily playlists</b>: the hours are made within minutes."]),
    ("h3", "…put the adverts last in the hour?"),
    ("steps", ["Pro → <b>Ad breaks</b> → <b>New</b>; give it a running order with a section.", "<b>Add row</b> <code>*:00:00</code> and click that time for every day.",
               "Choose <b>Ends at its time</b>: it ends exactly on the hour, the last thing of the hour before.", "Book the campaigns on the <b>Clients</b> tab. <b>Save</b>."]),
    ("h3", "…change a song in tomorrow's 09:00?"),
    ("steps", ["Pro → <b>Daily playlists</b> → ▶ to tomorrow → <b>09:00</b>.", "Select the song → <b>Another song</b>, or <b>Remove</b> it and double-click one in the library."]),
    ("h3", "…put a folder of songs in Gold?"),
    ("p", "Sintoniza Import with that folder, <i>into the category</i> Gold, no sub-categories. Or in the on-air program: Folders tab → right-click the folder → Category → Gold."),
    ("h3", "…send the ERC last month's music?"),
    ("steps", ["Pro → <b>ERC quotas</b> → <b>Last month</b>.", "Check the quotas; classify what is under <i>Songs to classify</i>.",
               "<b>Export for the ERC (XML)…</b>, then upload the file in the ERC's portal."]),
    ("h3", "…get the music report for the collecting society?"),
    ("p", "On-air program → <b>Reports</b> → <i>Music played (royalties)</i> → the month → <b>Save as CSV…</b>."),
    ("h3", "…send an advertiser the proof of broadcast?"),
    ("p", "<b>Reports</b> → <i>Adverts (proof of broadcast)</i> → the advertiser and the dates → <b>Save as a page to print…</b> → print to PDF."),
]),

("When something isn't right", "trouble", [
    ("table", ["What you see", "What to do"], [
        ["The on-air playlist doesn't match Daily playlists", "Press <b>Clear</b> in the on-air program: it refills from the plan, from the item planned for that moment. If it runs ahead or behind, the red line on Daily playlists says by how much; it catches up by the next hour."],
        ["Only jingles and adverts play", "The songs' files moved or were deleted: fix the categories' folders (Pro → Categories) and click <b>Scan</b> in the Library tab."],
        ["A song is skipped with “FileOpen”", "The file isn't where the library says: <b>Scan</b> again."],
        ["Listening is heard on air", "Settings → Audio → <b>Preview</b>: choose an output that isn't on air (headphones, PFL)."],
        ["Something went wrong on air", "Settings → Safety → <b>Open log folder</b>: one file a day. Settings → Alerts can warn you by Telegram or e-mail."],
    ]),
]),
]
