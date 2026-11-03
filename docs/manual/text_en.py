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
    ("p", "Sintoniza is radio automation for stations that stay on air around the clock. This chapter explains what each "
          "of its programs is for and how a day of radio is planned and played. Read it first, whatever your job at the station."),
    ("p", "Sintoniza is made of four programs. Three of them share one database, which we call the <b>library</b>. It holds "
          "the music and its categories, the schedule, the daily playlists and the history of everything that went on air."),
    ("icons", [
        ("radio-playout.png", "Sintoniza Emi", "The on-air program, in the studio. The name comes from <i>emissão</i>, Portuguese for broadcast. Presenters use it for the playlist, the cartwall, the microphone and the stream."),
        ("radio-playout-schedule.png", "Sintoniza Pro", "The station's production program. Whoever runs the station uses it for the settings, categories and cartwalls, the events and ad breaks, clients and campaigns, the hour templates for your shows and the daily playlists."),
        ("radio-playout-import.png", "Sintoniza Import", "Brings folders of music into the library and sorts them into categories."),
        ("radio-playout-rec.png", "Sintoniza Rec", "Records everything the station broadcasts and keeps it for as long as the law asks. It works on its own and doesn't need the library."),
    ]),
    ("p", "Each program has its own entry in the app menu on Linux, in the Start menu and on the desktop on Windows, and in "
          "Applications on a Mac. Only one copy of each can run on a computer at a time."),
    ("p", "If the library lives on a shared MySQL or MariaDB server, Pro and Import can run on other computers too. The "
          "commercial department can book adverts from its own office, and the music office can import new songs. The studio "
          "sees their changes within seconds."),
    ("tip", "The on-air program's <b>Settings</b> only holds what belongs to that one computer: its sound cards, the microphone, "
            "the database and the keys. Everything about the station itself, such as its name and logo, crossfades, fill music, "
            "the time signal, streaming, RDS, the website, alerts, categories and cartwalls, is set once in Sintoniza Pro and "
            "reaches every studio."),
    ("h2", "How the day is planned and played"),
    ("steps", [
        "<b>Import</b> the music into categories. Every station has eight fixed categories at the top, and your own go under them, such as Music › Current, Music › Gold or Music › 80s.",
        "In <b>Pro</b>, make an <b>hour template</b> for each of your shows. A template says which categories play, in which order. Place the templates on the week, then set up the <b>ad breaks</b> and book your <b>clients</b> into them.",
        "Sintoniza makes the <b>daily playlists</b> from the templates, for today and the next few days. You can look through them, swap songs by hand, or simply let the rules fill them.",
        "The <b>on-air program</b> plays the daily playlists. It starts the ad breaks and events at their times and keeps every hour on time.",
    ]),
]),

("First setup", "setup", [
    ("p", "This chapter is for whoever installs Sintoniza: usually the station's technician. Some settings are made on each "
          "studio computer, and the rest once for the whole station."),
    ("h2", "On each studio computer"),
    ("p", "Open the on-air program and click <b>Settings</b> at the bottom left, or press Ctrl+, (Cmd+, on a Mac). You will "
          "only find what belongs to this computer here. Click <b>Save</b> to apply your changes."),
    ("p", "Once the library is set up, Settings asks for a user who may change this computer's settings. At first that is "
          "<b>admin</b> with the password <b>admin</b>."),
    ("fig", "settings-station", "Settings → General", [("LanguageBox", "The <b>language</b> of every window and message: English, Português or Français. The change applies the next time the program starts.")]),
    ("p", "<b>Other languages.</b> You can add a language without waiting for a new version. Click <b>Add a language…</b> under "
          "the language list and Sintoniza opens its translations folder. Put a translation file there, named after its "
          "language, such as <i>de.json</i> for German or <i>es.json</i> for Spanish, and that language appears in the list. "
          "A file named after a built-in language, such as <i>pt.json</i>, corrects or completes its texts. Any text the file "
          "leaves out stays in English."),
    ("fig", "settings-audio", "Settings → Audio", [
        ("MusicDeviceBox", "The sound card and channel pair for <b>Music</b>. The <b>Cartwall</b> and <b>Preview</b> outputs are set just below. <b>Test</b> plays a short tone, so you can find the right fader on the desk."),
        ("PreviewDeviceBox", "<b>Preview</b> is for listening before something goes on air. Use headphones or the desk's PFL/cue input, never an output that goes on air.")]),
    ("fig", "settings-library", "Settings → Library", [
        ("LibraryTypeBox", "Where the library lives. <i>SQLite</i> is a file on this computer, fine for a single studio. <i>MySQL / MariaDB</i> is a server that several studios share."),
        ("TestLibraryButton", "<b>Test</b> opens the library. The first time, it also creates its tables.")]),
    ("h3", "Shared music on Windows, Mac and Linux"),
    ("p", "With a shared library, each song is stored with the path seen by the computer that found it. On a Linux studio "
          "that might be <i>/mnt/radio/Pop/song.mp3</i>. A Windows or Mac computer sees the same shared folder somewhere else, "
          "such as <i>Z:\\</i>, <i>\\\\server\\radio</i> or <i>/Volumes/radio</i>. You tell Sintoniza where, once, further "
          "down the Library tab."),
    ("fig", "settings-paths", "Settings → Library → This computer's paths", [
        ("PathMapRows", "On the left, how the library's paths start. On the right, where that folder is on this computer. Type it, or use <b>Folder…</b> to pick it."),
        ("AddPathMapButton", "<b>Add</b> a line. Sintoniza suggests where the library's songs start."),
        ("TestPathMapButton", "<b>Test</b> looks for a few of the library's songs on this computer…"),
        ("PathMapNote", "…and tells you how many it found. If it missed one, it says where it looked. When it finds them, click <b>Save</b>."),
    ], True),
    ("tip", "From then on, everything on that computer opens the files where they really are: playing, listening, waveforms, "
            "tags, scans, takes and voice tracks. Whatever it adds is stored with the library's path, so every studio finds it. "
            "Pro, Rec and Import on the same computer use this setting too. The share has to appear as a folder: a mapped drive "
            "or <i>\\\\server\\share</i> on Windows, a folder mounted under /mnt on Linux, or /Volumes on a Mac. An <i>smb://</i> "
            "address from the file manager won't do."),
    ("fig", "settings-safety", "Settings → Safety", [
        ("ResumeBox", "Three switches for a station that is never off air. With them on, Sintoniza carries on playing when it starts in AUTO, starts itself again after a crash, and starts when you log in to the computer."),
        ("BackupBox", "<b>Backups</b> of the database and the station's files, made every day.")]),
    ("h2", "Once, for the whole station"),
    ("p", "Open <b>Sintoniza Pro</b> and go to <b>Station settings</b>. Log in as <b>admin</b> with the password <b>admin</b>, "
          "and change that password straight away in Station → Users."),
    ("fig", "station-station", "Station settings → Station", [("StationNameBox", "The <b>station name</b>, <b>logo</b>, genre and website. They appear next to the clock and go out with the stream and RDS. "
        "Below them, the <b>station type</b>: <i>FM radio</i> if the microphones go through a mixing desk, <i>Web radio</i> if they go through the computer.")]),
    ("fig", "station-audio", "Station settings → Audio", [("CrossfadeBox", "How long one song overlaps the next, in seconds."),
        ("OtherCrossfadeBox", "The crossfade into or out of jingles, adverts and programmes. Zero gives a clean cut, which is usual."),
        ("LoudnessBox", "Plays every item at the same loudness, measured to the EBU R128 standard. Your files are never changed.")]),
    ("fig", "station-safety", "Station settings → Safety", [
        ("SilenceBox", "<b>Detect silence</b>. If nothing is heard for a while, an alarm goes off, and in AUTO the playout skips to the next item."),
        ("FillBox", "<b>Fill music</b> is what plays if the playlist ever runs out. The rest times below it, such as a song not again within so many hours, also apply to the daily playlists.")]),
    ("p", "The other tabs are covered in the chapters where you will need them. <b>Time signal</b> sets the pips at the top of the "
          "hour. <b>Library</b> lists extra music folders. <b>Streaming</b>, <b>RDS</b> and <b>Website</b> say where now playing "
          "is sent. <b>Alerts</b> chooses how you are warned when something goes wrong, and <b>Playlist</b> sets how many days of "
          "daily playlists are made ahead and the colours of the rows."),
    ("p", "Every studio takes the station's settings within a minute of you saving them. After an update, the first studio "
          "that starts the new version publishes its own settings as the station's."),
    ("tip", "Next, import the music (chapter 5), make the cartwalls, and build the hour templates and ad breaks (chapter 4). "
            "Then switch the on-air program to <b>AUTO</b> and press <b>Play</b>."),
]),

("Sintoniza Emi, the on-air program", "onair", [
    ("p", "Sintoniza Emi is the program the presenter works with in the studio. This chapter walks through its screen, the "
          "playlist and the cartwall, and then the things a technician sets up: GPIO, the web remote and backup computers."),
    ("p", "The top half of the screen shows what is on air now. The bottom half shows what comes next, in the playlist, and "
          "what you have at hand: the cartwall, the library and your folders."),
    ("h2", "Logging in"),
    ("p", "The program opens <b>locked</b>. You can see the on-air screen behind the log-in, but nothing responds to clicks or "
          "keys until a user with the <b>On air</b> permission logs in."),
    ("p", "Locked or not, the station stays on air. In <b>AUTO</b> the program starts playing by itself, the schedule and ad "
          "breaks run as planned, and the web remote keeps working."),
    ("fig", "main-locked", "Locked: log in to work on air", [
        ("UserBox", "The user. The last one to log in is already filled in…"),
        ("PasswordBox", "…so you usually only type the password."),
        ("LoginButton", "<b>Log in</b>, or press Enter."),
        ("LockNote", "A reminder that what is on air carries on meanwhile."),
    ]),
    ("tip", "<b>Log out</b>, at the bottom left, locks the screen again, for example between shows. While it is locked the window "
            "can't be closed. If the library can't be reached, there are no users to check, so the program opens without a "
            "log-in and the station can still be run."),
    ("h2", "The screen"),
    ("fig", "main", "The on-air screen", [
        ("OnAirBadge", "The state of the playout: <b>ON AIR</b>, <b>STOPPED</b>, <b>SILENCE</b> when nothing is heard, or a countdown to the next ad break."),
        ("NowTitle", "The title and artist on air."),
        ("WaveMain", "The waveform. The red part has already played. The thin bar underneath is the whole song."),
        ("Elapsed", "How long it has played. The <b>NEXT</b> item is shown below."),
        ("NoteBox", "<b>Notes for the presenter</b>, written in the daily playlists: for example, which sponsor to mention before the next song."),
        ("PlayButton", "<b>Play</b> (F5)."),
        ("PauseButton", "<b>Pause</b> and resume (F6)."),
        ("NextButton", "<b>Next</b> (F7) crossfades to the next item straight away."),
        ("StopButton", "<b>Stop</b> (F8)."),
        ("Remaining", "The time left of the item on air. It turns red in the last 10 seconds."),
        ("AutoSwitch", "<b>AUTO</b> runs through the playlist by itself. <b>ASSISTED</b> stops after each item and waits for you to start the next. In ASSISTED every song in the playlist gets a green <b>▶</b> to put it on air at once, and the song on air an orange <b>fade-out</b> button. Played songs have none (a double-click still plays one again)."),
        ("Clock", "The clock, with the station's name and logo."),
        ("TopOfHourText", "How long until the top of the hour."),
        ("PlaylistEndsText", "When the playlist will run out."),
        ("NextEventPanel", "<b>NEXT EVENT</b>: the next thing with a fixed time, such as the 9 o'clock news or an ad break. The countdown turns amber in the last minute and red in the last 10 seconds."),
        ("MeterL", "The output levels, left and right."),
        ("Playlist", "The playlist, explained on the next pages."),
        ("CartwallTab", "The <b>CARTWALL</b>, <b>LIBRARY</b> and <b>FOLDERS</b> tabs."),
        ("Cartwall", "The cartwall: jingles and sounds at hand."),
        ("SettingsButton", "<b>Settings</b> for this computer."),
        ("ReportsButton", "<b>Reports</b>: the music played and proof of broadcast for advertisers."),
        ("LogoutButton", "<b>Log out</b> locks the screen. What is on air carries on."),
        ("Lights", "The <b>status lamps</b>. Each small light shows how one part of the station is doing. They are explained just below."),
        ("LastLog", "The last thing that happened. Everything is also written to the log."),
    ]),
    ("h2", "Status lamps and the ON AIR sign"),
    ("p", "The status lamps tell you at a glance whether everything around the playout is working, so you don't have to go "
          "looking. By default they sit in the status bar at the bottom of the screen, as small lights with their names."),
    ("p", "The colours always mean the same: <b>green</b> is working, <b>amber</b> is a warning, <b>red</b> is a problem, and "
          "<b>grey</b> means that part is switched off or not used on this computer. This is what each lamp watches:"),
    ("table", ["Lamp", "What it is", "What the colours say"], [
        ["STUDIO", "Only shown when the station has more than one computer: which of them is on air.",
         "Green: this computer is on air (it says how many are standing by). Amber: this one is standby, another is on air."],
        ["STREAM", "The station's internet stream, which this computer sends to the Icecast or Shoutcast server.",
         "Green: sending, and for how long. Amber: connecting to the server. Red: it can't connect. Grey: off, or not set up."],
        ["LISTENERS", "How many people are listening to the stream, read from the server every minute. The number shows next to the lamp.",
         "Green: the number now. Amber (?): the server can't be read, or counting stopped. Grey: counting is off."],
        ["RDS", "The text FM radios show (the station's name and the song), sent to the RDS encoder.",
         "Green: sending. Red: the encoder doesn't answer. Grey: RDS is off, or this computer is standby."],
        ["WEBSITE", "Now playing on the station's website: the song on air sent to the website, to files or by FTP.",
         "Green: sent. Red: it can't send (the website or FTP server doesn't answer). Grey: off, or standby."],
        ["REC", "Sintoniza Rec, the program that records the broadcast (the recording the law asks for), on this computer.",
         "Green: recording. Amber: it is recording silence. Red: it is open but NOT recording. Grey: it isn't running."],
        ["LIBRARY", "The connection to the database with the songs, the playlists and the station's settings.",
         "Green: open. Red: it doesn't answer (the playout carries on with what it has). Grey: no library."],
        ["TODAY'S PLAYLIST", "The daily playlists for the hours left today, made in Sintoniza Pro.",
         "Green: all made. Amber: one is missing later on. Red: this hour's or the next one's is missing. Grey: no hour templates."],
        ["DEAD AIR", "Silence detection: the air going quiet for too long.",
         "Green: watching. Red: silence on air (in AUTO the next item plays). Grey: detection is off."],
        ["MIC", "The microphone, through the MIC button or the desk's mic fader (by GPIO).",
         "Red: the mic is open, on air. Grey: closed."],
        ["GPIO", "The connection to the mixing desk: the faders that start the players, and the desk's lights.",
         "Green: ready. Red: a connection can't be opened (the log says why). Grey: no GPIO set up."],
        ["ALERTS", "Warnings by Telegram, e-mail or web when something goes wrong (silence, the stream down, a full disk…).",
         "Green: on, nothing wrong. Red: a problem is still going on (point at it to see which). Grey: off."],
    ]),
    ("p", "Point at a lamp to read what it is doing, in full. Click it to open its settings, so you go straight to where a "
          "problem can be fixed."),
    ("p", "If you would rather see everything spelled out, the lamps can sit in a panel under the deck instead, each one with "
          "a line saying what it is doing."),
    ("p", "<b>The ON AIR sign.</b> When someone is live, a large red sign lights up in the station panel, between the clock "
          "and the level meters. It says:"),
    ("list", [
        "<b>MIC LIVE</b> while the microphone is open, whether through the <b>MIC</b> button or the desk's mic fader, read through GPIO;",
        "<b>ON AIR · Morning show</b>, with the show's name, during a live show;",
        "<b>ON AIR · LIVE INPUT</b> while a live input of the computer plays, such as a remote studio or the network news from a satellite receiver.",
    ]),
    ("p", "A GPIO output can light a real ON AIR lamp over the studio door at the same moment."),
    ("p", "<b>Big counters</b> make the time remaining, the cue points, the next event and the top of the hour much larger, so "
          "a presenter can read them from across the studio."),
    ("fig", "main-onair", "MIC LIVE from the mixing desk, with the big counters", [
        ("OnAirSign", "The red sign: <b>MIC LIVE</b>. This is an FM station, so the mic is on the desk and there is no MIC button: the desk's mic fader is up, and GPIO tells Sintoniza so."),
        ("Remaining", "With big counters on, the time remaining is shown large…"),
        ("NextEventCountdown", "…and so is the countdown to the next event."),
        ("Lights", "The status lamps."),
    ]),
    ("p", "Each studio computer chooses its own in <b>Settings → Screen</b>."),
    ("fig", "settings-screen", "Settings → Screen", [
        ("StatusPanelBox", "Ticked, the lamps sit in a panel under the deck, each saying what it is doing. Unticked, they are small lights in the status bar."),
        ("LampBoxes", "Which lamps are shown. Untick the ones this studio doesn't use, such as RDS on a web radio. What each lamp means is in the status lamps table above."),
        ("BigCountersBox", "<b>Big counters</b>, to read across the studio."),
        ("OnAirSignBox", "The <b>ON AIR sign</b>. Untick it if you don't want it on screen."),
    ]),
    ("h2", "The playlist"),
    ("p", "Each row in the playlist shows an icon for its type, the title and artist, the time it is expected to <b>start</b> "
          "and its <b>length</b>. The icons are ♫ for a song, ✦ for a jingle, € for an advert and 🎙 for a programme."),
    ("p", "Anything that isn't a song has its own background colour, so ad breaks and jingles stand out. The item on air fills "
          "with red as it plays, and a line marks where each hour starts."),
    ("fig", "main", "The playlist buttons", [
        ("OnAirButton", "<b>On air</b> jumps back to the item on air. It lights up when you scroll away."),
        ("HourViewButton", "<b>This hour</b> shows the hour before, this hour and the first half-hour of the next. Switch it off to see everything still to play."),
        ("AddStreamButton", "<b>Stream…</b> plays another station's stream, or a live input of this computer, such as the desk's line or a remote studio. Next to it are <b>Pause…</b>, <b>Requests…</b>, <b>Presenter view</b>, and <b>Open…</b> and <b>Save…</b> for M3U playlists."),
        ("RemoveButton", "<b>Remove</b> takes the selected items out. ▲ ▼ move them."),
        ("ClearButton", "<b>Clear</b> empties the playlist, except the item on air and anything the schedule locked. The plan then fills it again."),
    ], True),
    ("list", [
        "<b>Double-click</b> an item, or press Enter, to put it on air now.",
        "<b>Drag</b> rows to change the order. You can also drop songs here from the library, the Folders tab or your file manager.",
        "<b>Right-click</b> an item to edit its cue points (Ctrl+E), its segue (Ctrl+G) or its tags (Ctrl+T), or to remove it.",
        "Rows tagged <b>SCHEDULED</b> are ad breaks and events coming up. They are shown where they will play.",
        "A song tagged <b>WON'T PLAY</b> would start after its hour has ended. The playout will leave it out so the next hour starts on time.",
    ]),
    ("warn", "<b>Adverts are protected.</b> While an advert is on air, Next, Pause, Stop and putting another item on air all ask "
             "first. Only a click on the button inside that question goes ahead, so a paid spot is never cut by accident, but "
             "you still can in an emergency."),
    ("h3", "Pauses: time for the presenter to talk"),
    ("p", "A pause is an item with no sound. Use it when you want to talk between two songs, for example for the weather and "
          "traffic, and you want the playlist to wait for you."),
    ("p", "Click <b>Pause…</b> above the playlist. Write what you will talk about, then choose how long: a set time such as "
          "1:30, or <b>Until I press Next</b>. By default it goes right after the item on air."),
    ("p", "What you wrote becomes the pause's title. It is shown in the playlist, on air and in the notes when the pause is next, "
          "but it never goes out on RDS, the stream or the website. There is no crossfade into or out of a pause, and silence "
          "detection leaves it alone. To change a pause before it plays, right-click it and choose <b>Edit pause…</b>."),
    ("h3", "Where the songs come from"),
    ("p", "The songs come from the daily playlists, which are made in Sintoniza Pro (chapter 4). About half an hour before each "
          "hour, its songs are put into the playlist."),
    ("p", "Near the end of the hour, the playout keeps time. If the next hour would start late, it leaves out an item marked "
          "<i>optional</i>. If it would start early, it adds songs. You can still change the playlist on air whenever you like."),
    ("h2", "Cartwall"),
    ("p", "The cartwall holds the sounds a presenter fires by hand: station IDs, jingles, a traffic bed, the bakery's advert "
          "when you read it live."),
    ("fig", "main", "The cartwall", [
        ("Cartwall", "The carts, each with its name, icon and colour. The number in the corner is its key."),
        ("CartwallPicker", "Which cartwall is on screen. A station can have one for each show."),
    ], True),
    ("list", [
        "<b>Click</b> a cart to play it, and click again to stop it. Carts play over the music, on the Cartwall output.",
        "Each cart shows how long it lasts, and while it plays, how much is left.",
        "The cartwalls belong to the station. They are made in Sintoniza Pro → <b>Cartwalls</b>, and every studio gets them within a minute.",
        "You can give each cart a key in Settings → Shortcuts.",
    ]),
    ("h2", "Library"),
    ("p", "The Library tab lets you find any song in the station's library while you are on air."),
    ("fig", "main-library", "The Library tab (Ctrl+F)", [
        ("SearchBox", "Type to search every song by artist, title, album or file name. Capitals and accents don't matter."),
        ("CategoryBox", "Show only one category. A category includes the ones under it."),
        ("TrackList", "The songs. <b>Double-click</b> one, or press Enter, to add it to the end of the playlist. <b>Right-click</b> for more: Play next, its category, tempo, energy, tags and cue points, and <b>Not on the website</b>."),
        ("ScanButton", "<b>Scan</b> brings the library up to date with the music folders straight away."),
    ]),
    ("fig", "main-folders", "The Folders tab", [("Library", "The music folders on disk. Drag files or whole folders into the playlist. Right-click a folder and choose <i>Category</i> to put all its songs in a category. Songs added to that folder later get it too.")]),
    ("h2", "Cue points"),
    ("p", "Cue points tell Sintoniza where a song really starts and ends, and how long its intro lasts so you can talk over it. "
          "Right-click an item and choose <b>Edit cue points…</b>, or press Ctrl+E."),
    ("fig", "cue-editor", "Edit cue points…", []),
    ("list", [
        "<b>Cue In</b> is where playback starts, after any silence or count-in.",
        "<b>Intro</b> marks the end of the intro. While it runs, an INTRO countdown shows how long you can talk.",
        "<b>Outro</b> marks the start of the ending.",
        "<b>Mix</b> is where the next item starts while this one finishes. Use it for songs that end on a hit or a long tail.",
        "<b>Cue Out</b> is where playback stops, cutting off silence or a long fade.",
    ]),
    ("p", "<b>Auto-detect</b> finds the first and last sound and a good Mix point for you. Space plays and pauses on the Preview "
          "output. The arrow keys move one second, or a tenth with Shift held, and the mouse wheel zooms."),
    ("h2", "Segues"),
    ("p", "The segue editor shows exactly how one song runs into the next, and lets you shape the join by ear. Right-click a "
          "playlist item and choose <b>Segue with the next…</b> (Ctrl+G). In Sintoniza Pro, the same editor is under "
          "<b>Segue…</b> in Daily playlists."),
    ("fig", "segue", "Segue with the next…", [
        ("Join", "The end of the first song on top, the start of the next below, joined at zero. <b>Drag</b> either song to move the join. The yellow line is the first song's level as it fades."),
        ("MixText", "<b>Mix</b> is where the next song starts. Moving it creates a Mix marker, and the first song then plays on at full level to its Cue Out. <i>Automatic</i> goes back to the usual crossfade."),
        ("OutText", "<b>Cue Out</b>: where the first song stops."),
        ("InText", "<b>Cue In</b>: where the next song starts in its file. It can't go past its Intro."),
        ("PlayButton", "<b>Play the join</b> from a few seconds before, exactly as it will sound on air, on the Preview output (Space). Change something while it plays and you hear it again at once."),
        ("SummaryText", "What will happen, in plain words."),
    ]),
    ("p", "Your segue is saved as the two songs' cue points. It applies wherever those songs play, in every studio that shares the library."),
    ("h2", "Tags"),
    ("fig", "tag-editor", "Edit tags…", []),
    ("p", "<b>Edit tags…</b> (Ctrl+T) changes the title, artist, album, genre, year, track number, comment and cover picture "
          "stored in the file itself. A file that is on air can't be changed until it has finished."),
    ("h2", "Reports"),
    ("p", "Everything that goes on air is logged. Reports turns that log into the documents a station is asked for: the music "
          "list for the collecting society, and proof of broadcast for advertisers."),
    ("fig", "reports-adverts", "Reports", [
        ("KindBox", "Which report: <b>Music played</b> for royalties, <b>Music totals per song</b>, <b>Adverts</b> as proof of broadcast, <b>Everything</b>, the full as-run log (with the listeners when each item started, once they are counted), or <b>Listeners per hour</b>."),
        ("AdvertiserBox", "For proof of broadcast, one advertiser or all of them."),
        ("HtmlButton", "<b>Save as a page to print…</b>, which you can also print to PDF. Next to it, <b>Save as CSV…</b> gives you a spreadsheet."),
    ]),
    ("h2", "Live shows, the presenter view and requests"),
    ("p", "When a presenter is live, they want a calm screen with only what matters, and a simple way to deal with listeners "
          "who call in. That is what the presenter view and the requests list are for."),
    ("p", "<b>Live shows</b> are set up in Sintoniza Pro → <b>Shows</b> (chapter 4). While a show is on, the playout switches "
          "to ASSISTED, so each item waits for the presenter to press Play. The presenter view opens by itself, and the show "
          "and the presenter's name go out on RDS and the website. When the show ends, the playout goes back to AUTO and, if "
          "nothing is on air, starts the playlist. Ad breaks and events play as usual throughout."),
    ("p", "<b>The presenter view</b> is a large, simple screen. It opens with a live show, or with the <b>Presenter view</b> "
          "button above the playlist. It shows:"),
    ("list", [
        "the show and how long until it ends;",
        "what is on air, with its time left and a <b>TALK UP</b> countdown to the end of the intro;",
        "the next three items;",
        "the notes: the item's, a pause's and the show's;",
        "countdowns to the next ad break and the top of the hour;",
        "the requests waiting, and big <b>PLAY</b>, <b>NEXT</b> and <b>STOP</b> buttons.",
    ]),
    ("p", "<b>Listener requests.</b> When a listener phones or sends a message asking for a song, whoever takes it clicks "
          "<b>Requests…</b> above the playlist, or ♥ in the web remote. Find the song in the library, write who it is for and "
          "their message, for example <i>Ana, from Braga: happy birthday to João</i>, and click <b>Take the request</b>."),
    ("p", "The presenter sees the request waiting. <b>▶ Play next</b> puts it right after the item on air, with who it is for "
          "and the message as its note, so it is in front of them when the song starts. <b>Turn down</b> drops it. The "
          "requests list belongs to the studio, and nothing is open to the internet."),
    ("h2", "The microphone (web radio)"),
    ("p", "On an FM station the microphones go through the mixing desk, and Sintoniza doesn't handle them. On a web radio, with "
          "<i>Web radio</i> chosen in Pro → Station settings, the microphone goes through the computer instead."),
    ("p", "The <b>MIC</b> button (F9) then opens the microphone, and the music and carts fade down while it is open. You can "
          "hold it to talk, or set it in Settings → Microphone to press once to open and again to close."),
    ("fig", "settings-microphone", "Settings → Microphone", []),
    ("h2", "GPIO: the desk's faders and lights"),
    ("p", "GPIO connects Sintoniza to your mixing desk. Pushing up a fader can start the next song, the mic fader can light "
          "the MIC LIVE sign, and the playout can light lamps on the desk or over the studio door. You set it up in "
          "<b>Settings → GPIO</b>, using a serial port, the network or MIDI. You can use all three together."),
    ("fig", "settings-gpio", "Settings → GPIO", []),
    ("h3", "A serial port"),
    ("p", "Sintoniza uses a serial port's control lines, and a USB serial adapter works fine. There are three inputs: "
          "<b>CTS</b> (pin 8), <b>DSR</b> (pin 6) and <b>DCD</b> (pin 1). Each one is a contact between that pin and DTR "
          "(pin 4) or RTS (pin 7)."),
    ("p", "For each input you choose what happens when the contact <b>closes</b> and when it <b>opens</b>:"),
    ("list", [
        "<b>Play (fader start)</b>, <b>Next</b>, <b>Stop</b> or <b>Pause / carry on</b>;",
        "a <b>cart</b> of the cartwall on screen;",
        "<b>Mic fader up (MIC LIVE)</b> and <b>Mic fader down</b>, to light and put out the MIC LIVE sign from the desk's mic fader;",
        "or nothing.",
    ]),
    ("p", "The two outputs, <b>DTR</b> and <b>RTS</b>, switch on while something is playing, while an advert is on air, while "
          "a live input or stream is on air, while there is silence, or with <b>the ON AIR sign</b>, which is how you light "
          "a real studio lamp. An output you don't use is held on, to power the input contacts."),
    ("tip", "The usual fader start: CTS closes and the playout plays; when it opens again, as the fader comes down, choose Next "
            "or nothing. A contact that bounces counts only once, and Next, Stop and Pause are not done while an advert is on "
            "air. <b>Test the inputs</b> shows each one change as you move the faders."),
    ("h3", "The network and MIDI"),
    ("p", "<b>Network</b> is for a GPIO-to-network box or a desk that sends text messages over TCP or UDP. Each <b>text</b> "
          "that arrives does something, such as FADER1_ON for Play. Sintoniza also <b>sends</b> texts when a signal starts and "
          "ends, such as ONAIR_ON and ONAIR_OFF. <b>Only from</b> lists the addresses allowed to send, because anyone who "
          "can reach the port could press Play."),
    ("p", "<b>MIDI</b>, on Linux and Windows, is for a digital mixer or a controller. Each <b>note</b> or <b>controller</b> "
          "does something when pressed and when released, and Sintoniza can light the controller's buttons by sending notes."),
    ("p", "For both, <b>Listen</b> shows what arrives as you press a button or move a fader, and <b>Add the last received</b> "
          "puts it in the list, so there is nothing to type."),
    ("h2", "Web remote"),
    ("p", "The web remote lets you control the playout from a phone or another computer on the station's network. It is a web "
          "page, so there is no app to install."),
    ("fig", "settings-remote", "Settings → Remote", [
        ("RemoteBox", "Switch it on. It is off until you do."),
        ("RemotePortBox", "The port, 8090 by default. The page asks for a station user with the <b>Web remote</b> permission, such as <b>dj</b>."),
        ("RemoteAddressText", "The address to open on the phone. The phone must be on the same network."),
    ]),
    ("p", "The page is laid out like the on-air screen: the deck with the waveform, the station panel with the clock and the "
          "level meters, the playlist, and the cartwall and library."),
    ("fig", "remote-desktop", "The web remote on a computer", []),
    ("list", [
        "You see what is on air, with its waveform, time played and time left, the talk-over and ad break countdowns, the clock, the next event and the levels.",
        "<b>▶ ❚❚ ⏭ ■</b> and <b>AUTO</b> work as on screen. An advert on air is only cut after a question.",
        "Tap an item in <b>the playlist</b> for <i>▶ Now</i>, which puts it on air, or <b>✕</b> to remove it.",
        "<b>CARTWALL</b> fires carts with a tap. <b>LIBRARY</b> searches the library to add a song after the item on air, or ♥ to take a listener's request.",
    ]),
    ("fig", "remote-phone", "On a phone, the same panels one under the other", []),
    ("warn", "The web remote uses plain HTTP and is meant for the station's own network. From outside, use it only through the "
             "station's VPN, and never open its port on the router. Five wrong passwords lock the page for a minute."),
    ("h2", "Several computers: one on air, the others on standby"),
    ("p", "Many stations keep a second computer ready in case the first one fails. With a shared MySQL library, every computer "
          "running Sintoniza Emi for the station knows about the others."),
    ("p", "<b>One computer is on air.</b> The others are on <b>standby</b>: ready and silent. They have no playlist, play no "
          "time signal, send no stream and write nothing to the play history. The first one to start goes on air. On that "
          "computer, the STUDIO lamp lists the ones on standby, and Sintoniza Pro's status bar shows them too."),
    ("fig", "main-standby", "A computer on standby", [
        ("StandbyBanner", "An amber <b>STANDBY</b> bar across the top."),
        ("StandbyText", "The first line says which computer is on air and what it is playing. The second says what this one will do: <i>This computer goes on air automatically if the master stops answering for 15 s</i>, or that it goes on air only when you press <b>Take on air</b>."),
        ("TakeOnAirButton", "<b>Take on air</b>, or Play, puts this computer on air after asking. The other one stops and goes to standby. In AUTO, it starts the hour from the item planned for now."),
    ], True),
    ("fig", "settings-studios", "Settings → Safety → Several computers on air", [
        ("TakeOverBox", "With this on, a standby computer goes on air by itself when the one on air hasn't answered for a while, because it crashed, was switched off or lost the network."),
        ("TakeOverSecondsBox", "How long to wait, in seconds: 15 to start with. Switch it off on a computer that should never take over, such as a production computer that happens to have Emi open."),
    ], True),
    ("warn", "A computer cut off from the network keeps playing, because it can't know another one has taken over. Until the "
             "network is back, both play. Then it finds the other on air and goes to standby. Carts still play on a standby "
             "computer, but they don't go in the play history. Every studio must run the same version: the first one to start "
             "a new version updates the database, and an older one can't open it."),
    ("h2", "All the settings"),
    ("p", "Here is where each setting lives. These belong to one computer, in the on-air program's <b>Settings</b>:"),
    ("table", ["Tab", "What it is for"], [
        ["General", "The language of the screens, and adding a language"],
        ["Audio", "The outputs for Music, Cartwall and Preview"],
        ["Microphone", "On a web radio: the input, the levels and how far the music goes down"],
        ["Safety", "What happens at start-up and after a crash, several computers on air, the log and backups"],
        ["Library", "The database, automatic scans, and this computer's paths to shared music"],
        ["Shortcuts", "Keys for Play, Pause, Next, Stop, the mic, the stream and each cart"],
        ["RDS", "Whether this computer sends to the RDS encoder, and how it reaches it"],
        ["Playlist", "How much of the next hour the playlist shows"],
        ["Screen", "The status lamps, big counters and the ON AIR sign"],
        ["GPIO", "The desk's faders and lights, by serial port, network or MIDI"],
        ["Remote", "The web remote: on or off, its port and its address"],
        ["About", "The version, updates and this manual"],
    ]),
    ("p", "These belong to the whole station, in <b>Sintoniza Pro → Station settings</b>:"),
    ("table", ["Tab", "What it is for"], [
        ["Station", "Name, logo, genre, website, FM or web radio, and the money sign"],
        ["Audio", "Crossfades and loudness levelling"],
        ["Time signal", "The pips or your own file at the top of the hour, and hour announcements such as HRS05.mp3 at 05:00"],
        ["Takes", "The folder of takes for the time and temperature, and where the station is"],
        ["Safety", "Silence detection and fill music"],
        ["Library", "Other music folders, and the voice tracks folder"],
        ["Streaming", "The Icecast or Shoutcast server, format and bitrate, and counting the listeners"],
        ["RDS", "PS, radio text and RT+"],
        ["Website", "Now playing for the station's website, as JSON, files or FTP"],
        ["Alerts", "Telegram, e-mail or web alerts for silence, a stream down, a crash, a full disk, the clock, backups, or tomorrow's daily playlists not ready"],
        ["Playlist", "How many days of daily playlists are made ahead, and the row colours"],
    ]),
]),

("Sintoniza Pro", "schedule", [
    ("p", "Sintoniza Pro is where the station is run from: the programme director, the music office and the commercial "
          "department all work here. This chapter covers the library and its categories, the cartwalls, users, the website, "
          "and then the schedule: events, shows, ad breaks, clients, hour templates and the daily playlists."),
    ("p", "Pro opens on <b>Home</b>, which shows the station at a glance and anything that needs your attention."),
    ("fig", "production", "Sintoniza Pro: Home", [
        ("MainMenu", "The <b>menu</b>: File, Library, Programming, Traffic, Station and Help."),
        ("SaveButton", "The <b>toolbar</b>, with Save (Ctrl+S) and the tools you use most."),
        ("NavPanel", "The <b>tools</b>, grouped as on the menu: the library, the programming, traffic for advertising, and the station."),
        ("HomeClock", "The clock and the date."),
        ("HomeToday", "<b>Today</b>: the hours planned, the ad breaks and adverts booked, events and voice tracks."),
        ("HomeWarnings", "<b>Needs attention</b>: missing files, songs without a category, hours without a template, campaigns ending this week, unsaved changes, and anything not ready in <b>tomorrow's daily playlists</b> (see below)."),
        ("HomeComputer", "<b>This computer</b>: system, processor, memory, free disk and how long it has been on. Next to it, the library and the database."),
        ("NowTitleText", "<b>Now playing</b> on air, from every studio, and this hour's template."),
        ("BoxStationText", "The station, the database and who is logged in."),
        ("StatusVersion", "The <b>status bar</b>: the version, station, database, user and any unsaved changes…"),
        ("StatusClock", "…and the date and time."),
    ]),
    ("p", "With a shared library, the status bar also says which computer is on air and which are on standby (chapter 3)."),
    ("p", "<b>Tomorrow's daily playlists</b> are looked over every ten minutes, so a problem is found the day before, not on air. "
          "Home tells you, in a line each, about an hour that has a template but no daily playlist yet, an hour that came out "
          "empty because its template found no music, an hour more than three minutes short (fill music would play the rest), "
          "a song whose file isn't there, and an ad break or event that would leave something out, such as an advert or a "
          "news file that can't be found. A news folder that gets a fresh file each hour isn't counted: it will have one by then. "
          "The computer on air can also send this as an alert, from 18:00 or the hour you set in <b>Station settings → "
          "Alerts</b>, so someone can fix it before the evening is over. Alerts must be on there, with Telegram, e-mail or a "
          "web address."),
    ("tip", "If Home says none of tomorrow's files can be reached from this computer, either the music share is down or this "
            "office computer sees the music somewhere else than the studios do. Set where it is in the playout's "
            "<b>Settings → Library → This computer's paths</b>. The files are then checked from here too."),
    ("h2", "Songs"),
    ("p", "<b>Songs</b> shows the whole library. You can search it, sort songs into categories, give them a tempo and an energy, "
          "and fix their tags. Right-click a song for its category, restrictions, ERC details, <b>Edit tags…</b> and "
          "<b>Edit cue points…</b>."),
    ("fig", "pro-songs", "Songs: listening to a song", [
        ("SearchBox", "Search by title, artist or album…"),
        ("CategoryBox", "…within a category, including the categories under it."),
        ("TrackList", "<b>Double-click</b> a song, or select it and press Space, to listen to it."),
        ("ListenButton", "▶ / ❚❚ and ■: play, pause and stop."),
        ("ListenSlider", "Drag to jump to another moment."),
        ("ListenTitle", "What is playing. You hear it on this computer's <b>Preview</b> output, never on air."),
    ]),
    ("tip", "<b>Edit cue points…</b> opens the same editor as in the studio. Your changes are saved in the library for every "
            "studio, the playout picks them up within a minute, and the daily playlists count the new length."),
    ("p", "<b>Not on the website.</b> Some things shouldn't appear on the station's website, such as a song played for a "
          "competition, whose title would give the answer away. Select them, right-click and tick <b>Not on the website</b>. While one of "
          "them plays, the website keeps showing what it showed before."),
    ("h2", "Categories"),
    ("p", "Categories sort the library so the hour templates can say \"a current hit, then a gold song, then a jingle\". Every "
          "station has the same eight categories at the top, which can't be renamed, moved or deleted:"),
    ("list", [
        "<b>Music</b>, <b>Jingles</b>, <b>Promos</b> and <b>Fillers</b>;",
        "<b>ProgAutor</b>, for shows recorded outside the station;",
        "<b>Redação</b>, for the news;",
        "<b>Spots</b>, for adverts;",
        "<b>Takes</b>, for voice inserts such as the time and the temperature, as if a presenter were there.",
    ]),
    ("p", "Your own categories go under them with <b>New under it</b>, such as Music › 80s or Spots › Clients, or beside them "
          "at the top with <b>New at the top</b>. You can rename, move and delete your own categories freely."),
    ("fig", "categories-fixed", "Categories: one of the station's eight", [
        ("CategoryList", "The categories, as a tree. A 🔒 marks one of the station's eight."),
        ("FixedPanel", "What it is for. Only its colour and folders can change."),
        ("AddChildButton", "<b>New under it</b> makes a category of your own under the selected one."),
    ]),
    ("fig", "categories", "Categories: one of your own", [
        ("ParentBox", "<b>Under</b> moves it inside another category."),
        ("Swatches", "Its colour in the lists."),
        ("FolderList", "Its <b>folders</b>. Every song in them belongs to this category, and so does every song you put there later."),
        ("AddFolderButton", "Add a folder."),
        ("ApplyButton", "<b>Save changes</b>."),
    ]),
    ("h2", "Cartwalls"),
    ("p", "The cartwalls are made here once and appear in every studio. A station might have one for the morning show and "
          "another for the weekend."),
    ("fig", "cartwalls", "Cartwalls", [
        ("PageList", "The cartwalls, with <b>New</b>, <b>Copy</b>, <b>Rename…</b> and <b>Delete</b>."),
        ("CartGrid", "Its 12 carts, as the studio sees them. Click one to change it, or drop a file on it."),
        ("ChooseFileButton", "The cart's sound: <b>Choose file…</b>, <b>Listen</b> on the Preview output, or <b>Clear</b>."),
        ("ColorPanel", "Its colour…"),
        ("IconPanel", "…and its icon."),
        ("SaveButton", "<b>Save</b>. Every studio gets the change within a minute."),
    ]),
    ("tip", "As with the music, use files that every studio can reach, in a shared folder."),
    ("h2", "Several stations in one library"),
    ("p", "One library can hold several stations, for example a music station and a news station of the same group. "
          "They share the music and the users. Each keeps its own schedule, daily playlists, settings, cartwalls and history."),
    ("list", [
        "<b>Station → New station…</b> in Sintoniza Pro makes one. It starts with a copy of the current station's settings and an empty schedule. Its stream server, RDS name and website upload start blank, so it never goes out as the other station.",
        "<b>Station → Work on</b> switches Pro to another station. Pro starts again on it. The studio's on-air program on the same computer keeps its own station.",
        "In each studio, <b>Settings → General → Station</b> chooses the station it puts on air. It applies the next time Sintoniza Emi starts.",
        "In <b>Users</b>, a user can be limited to some stations. Logging in to another station is then refused.",
    ]),
    ("h2", "Users and permissions"),
    ("p", "Every program asks for a <b>user and password</b>: the on-air program, Sintoniza Pro, Sintoniza Import, the on-air "
          "program's Settings and the web remote. They are the station's users, the same in every studio."),
    ("p", "A new library comes with two: <b>admin</b> with the password <b>admin</b>, who may do everything, and <b>dj</b> "
          "with the password <b>dj</b>, a presenter who may work on air, record voice tracks and use the web remote. "
          "<b>Change both passwords first.</b> Until you do, Pro's Home reminds you."),
    ("fig", "login", "Logging in", [
        ("UserBox", "The user. Pro remembers the last one…"),
        ("PasswordBox", "…and the password."),
        ("LoginButton", "<b>Log in</b>. Later, File → <b>Switch user…</b> changes the user, and File → <b>Log out</b> covers Pro with the log-in again."),
    ]),
    ("fig", "users", "Station → Users", [
        ("UserList", "The station's users."),
        ("AddButton", "<b>New user</b>, with <b>Delete</b> next to it."),
        ("NameBox", "Their name…"),
        ("PasswordBox", "…and a new password. Leave it empty to keep the old one."),
        ("EnabledBox", "<b>May log in</b>. Untick it to stop someone without deleting them."),
        ("AdminPresetButton", "<b>Everything</b> or <b>Presenter</b> tick the usual sets of permissions in one go."),
        ("PermissionPanel", "What they may use. Pro only shows them those tools, menus and buttons."),
        ("SaveUserButton", "<b>Save user</b>."),
    ]),
    ("table", ["Permission", "Lets them"], [
        ["On air", "Work the on-air program in the studio: playing, the playlist, the cartwall, the microphone"],
        ["Library", "Manage songs and categories, and import music, including with Sintoniza Import"],
        ["Programming", "Make hour templates, daily playlists and events"],
        ["Voice tracks", "Record voice tracks. They see the daily playlists but can't change the songs."],
        ["Traffic", "Manage ad breaks, clients and campaigns"],
        ["Station", "Change the station's settings and cartwalls"],
        ["Users", "Add and change users"],
        ["This computer's settings", "Open a playout's own Settings: sound cards, database, keys"],
        ["Web remote", "Control a playout from a phone"],
    ]),
    ("tip", "Passwords are only stored as a secure hash, so nobody can read them. If someone forgets theirs, another user with "
            "the <b>Users</b> permission sets a new one. Nobody can delete or lock out themselves, or take away their own "
            "<b>Users</b> permission."),
    ("h2", "Now playing on the station's website"),
    ("p", "Most stations show the song on air on their website, and often in their streaming player. Sintoniza sends it "
          "every time what is on air changes. You set it up once in <b>Station settings → Website</b>. Only the computer "
          "on air sends it, so a standby studio never interferes."),
    ("fig", "station-website", "Station settings → Website", [
        ("WebUrlBox", "The address of the page that receives now playing. Sintoniza posts it there as JSON: the station, what is on air and what is next, and the live show. The <b>key</b> below it goes with every post, so the page can refuse anyone else. Leave the address empty if you only want the files further down."),
        ("WebPostTestButton", "<b>Test</b> posts once to this page, with the key, and tells you what it answered: <i>The page took it</i>, or why not (a wrong key gives 403)."),
        ("WebCoverBox", "Send the song's cover picture too, when the file has one. Just above, <b>Songs only</b> keeps the last song on the website during jingles, adverts and programmes."),
        ("WebDelayBox", "<b>Hold every change back</b> by this many seconds. Set it to your stream's delay, so the website changes when listeners actually hear the new song."),
        ("WebOutputsPanel", "<b>Files and FTP</b>: each one is written whenever now playing changes."),
        ("AddWebTextButton", "<b>Add a text</b> written from a template…"),
        ("AddWebCoverButton", "…or <b>Add the cover picture</b>."),
    ]),
    ("p", "<b>A text</b> is built from a template that you write, with fields that are filled in each time. For example, "
          "<code>{artist} - {title}</code> gives a line such as <i>Luna Vale - Gravity</i> for the website or a streaming "
          "encoder. The fields are {artist}, {title}, {type}, {duration}, {started}, {next_artist}, {next_title}, {station}, "
          "{show} and {presenter}. You can also write a whole XML or HTML page, and the fields are made safe for it. When "
          "nothing is on air, {title} is the station's name."),
    ("p", "<b>The cover picture</b> is written as a picture file. For songs with no cover, choose a picture to send instead, "
          "usually the station's logo."),
    ("p", "Each one can go to three kinds of place:"),
    ("list", [
        "<b>A file</b>, on this computer or on a shared folder;",
        "an <b>FTP</b> server, with an address such as <i>ftp://server/folder/song.txt</i>, or <i>ftps://</i> for FTP over TLS, with its user and password;",
        "a web page that takes it by <b>HTTP PUT</b>. The example PHP page that comes with Sintoniza does this; its key goes in the password box.",
    ]),
    ("p", "Each text and the cover has its own <b>Test</b> button, at the top of its box. It writes that one once, to its file, "
          "FTP server or PHP page, and says underneath whether it worked or what went wrong. The test sends what is on air "
          "now, with the same rules as on air: with <b>Songs only</b> a jingle or an advert isn't sent, nor is a song marked "
          "<i>Not on the website</i>. If nothing is on air, a text gets the station's name, never made-up test words on your "
          "website. The cover test sends the cover of the song on air, or else the picture for songs without a cover; with "
          "neither, it says nothing was written, as on air. The tests work in Sintoniza Pro too, so you can set everything "
          "up from the office. From Pro they don't know what is next or the live show; the studio sends those with the next song."),
    ("p", "In the studio, the on-air program also has <b>Send now</b>, which sends to all of them at once. If the website or "
          "the FTP server can't be reached, Sintoniza tries again every 30 seconds, and the WEBSITE lamp in the studio turns red."),
    ("tip", "Songs marked <b>Not on the website</b> in the library are never sent. A pause isn't either. The website simply "
            "keeps what it showed before."),
    ("h2", "ERC quotas (Portuguese music)"),
    ("p", "This section is for Portuguese stations. The Lei da Rádio (articles 41 to 47, as changed in 2024) asks that, of "
          "the songs played in a month, at least <b>30 %</b> are Portuguese music. Of that Portuguese music, at least <b>60 %</b> "
          "must be sung in Portuguese by EU citizens, and at least <b>35 %</b> must have first come out in the last 12 months."),
    ("p", "The quotas count over the whole month and also <b>Monday to Friday, from 7:00 to 20:00</b>. Every month the "
          "station sends the ERC what it played, through the ERC's portal."),
    ("p", "For this, each song needs four details: whether it is Portuguese music, the language it is sung in, the performer's "
          "country and when it first came out. You can give them to a whole category in <b>Categories</b>, and its songs and "
          "the categories under it take them. Or give them song by song in <b>Songs</b>: right-click and choose "
          "<b>ERC classification…</b>."),
    ("fig", "erc-song", "ERC classification of a song", [
        ("PortugueseBox", "<b>Portuguese music</b>: yes, no, or whatever its category says."),
        ("LanguageBox", "The <b>language</b> it is sung in, such as pt-PT, pt-BR or en."),
        ("CountryBox", "The <b>performer's country</b>, from the station's list with the ERC's codes."),
        ("ReleaseBox", "The <b>first edition</b>, as a year, a month or a full date. Left empty, the year in the file's tags is used."),
    ]),
    ("fig", "erc", "Programming → ERC quotas", [
        ("FromBox", "The period: <b>Last month</b>, <b>This month</b> or any dates…"),
        ("CheckButton", "…then <b>Check</b>."),
        ("AllPanel", "The three quotas over the whole period, green when met and red when not, with the counts."),
        ("DaytimePanel", "The same for Monday to Friday, 7:00 to 20:00. While the period isn't over, a third card adds in the daily playlists still to play."),
        ("MissingList", "<b>Songs to classify</b>: songs played or planned without the ERC details. Until you classify them they count as not Portuguese."),
        ("ClassifyButton", "<b>Classify…</b> the selected songs yourself, or <b>Look up on MusicBrainz…</b> to fetch their country, language and first edition from the internet. You untick anything that looks wrong and save the rest."),
        ("AddToLibraryButton", "Songs that were played from files outside the library can be added to a category here, so they can be classified."),
        ("ExportButton", "<b>Export for the ERC (XML)…</b> writes the file the ERC's portal takes."),
        ("AliasBox", "The programme service's name at the ERC, its ALIAS."),
        ("CountryPanel", "The countries and their ERC codes, and which are in the EU. Portugal is 045; copy the others from the portal."),
    ]),
    ("tip", "The daily playlists can only plan for the quotas if they have <b>classified songs</b> to choose from. A quick way to "
            "start: in <b>Songs</b>, select a category's songs, right-click and choose <b>ERC: look up on MusicBrainz…</b>. It "
            "takes about three seconds a song."),
    ("p", "<b>Planning to the quotas</b> is on by default. Each hour of the daily playlists is then made to meet the three "
          "quotas on its own. When there are too few Portuguese songs, about every third song is Portuguese, favouring songs in "
          "Portuguese by EU performers and recent ones where those fall short. The songs still come from each line's category "
          "and follow the other rules."),
    ("h2", "Listeners"),
    ("p", "How many people listen to the stream? Sintoniza can count them. Tick <b>Count the listeners</b> in "
          "<b>Station settings → Streaming</b>. Every minute, the computer on air asks the Icecast or Shoutcast server how many "
          "are listening and keeps the number in the database. The server's public statistics are enough, no password needed."),
    ("p", "If your stream comes from another encoder, not from Sintoniza, type the server's address under <b>Statistics from</b>, "
          "for example <code>http://stream.example.com:8000</code>. With more than one mount point, say an MP3 and an AAC stream, "
          "list them: <code>/live, /live.aac</code>. They are added up, and <code>*</code> counts every mount on the server."),
    ("p", "In the studio, the <b>LISTENERS</b> lamp in the status bar shows the number now. In Sintoniza Pro, <b>Station → "
          "Listeners</b> shows a day hour by hour: each bar is the hour's average, and pointing at it gives the most and the "
          "fewest. Click an hour to see what played in it, with how many were listening when each song started. So you can see "
          "which shows and which songs keep people listening. The Home page shows the listeners now too."),
    ("tip", "A standby computer doesn't count, so the same listeners are never counted twice. If the computer on air stops, the "
            "one that takes over starts counting."),
    ("h2", "Song restrictions"),
    ("p", "Restrictions say when a song may be picked automatically. A Christmas song only in December, say, or a song with "
          "strong lyrics only after 22:00. In <b>Songs</b>, right-click and choose <b>Restrictions…</b>."),
    ("p", "The daily playlists always respect restrictions, and fill music respects the dates, days and hours. A song you put "
          "in by hand plays all the same. Restricted songs show ⏱ in the list."),
    ("fig", "restrictions", "A song's restrictions", [
        ("FromBox", "<b>Only from</b>…"),
        ("UntilBox", "…<b>until</b>. Leave either empty for no limit."),
        ("DayGrid", "On these <b>days</b>…"),
        ("HourGrid", "…at these <b>hours</b>. A lit hour is allowed."),
        ("DayHoursButton", "Quick choices: all hours, 7:00 to 20:00, or 20:00 to 7:00."),
        ("MaxDayBox", "At most so many times a <b>day</b>…"),
        ("MaxWeekBox", "…and a <b>week</b>, Monday to Sunday. Zero means no limit."),
        ("RemoveButton", "<b>No restrictions</b> clears them all."),
    ]),
    ("h2", "Takes: the time and the temperature"),
    ("p", "Takes are short recordings, like \"It's ten fifteen\" or \"Eighteen degrees in the city\". The playout puts them "
          "together at the moment they go on air, so they are always right, as if a presenter were in the studio."),
    ("p", "To use them, add a line to an hour template with <b>Add a line → 🕒 Time / temperature…</b> and choose the time, "
          "the temperature, or both."),
    ("fig", "station-takes", "Station settings → Takes", [
        ("TakesFolderText", "The <b>folder</b> with the recordings: HRS00 to HRS23 for the hours, MIN01 to MIN59 for the minutes, and TMP0 to TMP45 for the temperature, with TMPM1 and so on below zero. A whole time, such as HRS10_15, works too."),
        ("TakesFolderNote", "What Sintoniza found in the folder."),
        ("LatitudeBox", "Where the station is, so it can read the local temperature. It comes from Open-Meteo, free, every 10 minutes…"),
        ("TemperatureButton", "…and <b>Read the temperature</b> lets you try it."),
    ]),
    ("tip", "The files can be in subfolders, such as Hours, Minutes and Temperature. If you record the same take more than once, "
            "say HRS10a and HRS10b, Sintoniza picks one at random, so it sounds less repetitive. The time said is the nearest "
            "minute when the take starts. If a file is missing, the take is skipped and the log says which file it needed."),
    ("h2", "The schedule"),
    ("p", "The schedule is everything planned ahead: events, shows, ad breaks, clients and campaigns, hour templates and the "
          "daily playlists. You choose each one on the left."),
    ("p", "<b>Save</b> in the toolbar lights up when something has changed. It writes your changes to the database, and the "
          "on-air program picks them up within a few seconds. If someone else saved in the meantime, Pro asks before replacing "
          "their changes."),
    ("tip", "<b>The week calendars</b> work the same everywhere. <b>Click a cell</b> to place the selected item at that day and "
            "time, and click again to take it out. <b>Drag</b> to fill many cells at once, or <b>click a time</b> to fill it "
            "for every day. <b>Add row</b> adds an exact time, such as <code>12:59:55</code>; <code>*:59:55</code> means that "
            "minute and second of every hour."),
    ("h2", "Events"),
    ("p", "An event is anything that has to play at a set time: the 9 o'clock news, a horoscope every morning, a network show "
          "from a satellite receiver."),
    ("fig", "schedule-events", "Events: things that play at set times", [
        ("EventList", "The events, with <b>New</b> and <b>Delete</b> below them."),
        ("EventGrid", "The week, showing where the selected event plays."),
        ("EventAddTimeBox", "An exact time…"),
        ("EventAddTimeButton", "…to add as a row."),
        ("SaveButton", "<b>Save</b> the whole schedule."),
    ]),
    ("p", "An event can play:"),
    ("list", [
        "a <b>file</b>;",
        "the <b>newest file in a folder</b>, ideal for news bulletins that are downloaded there. It can be skipped if the newest file is older than so many minutes, so yesterday's news never goes out;",
        "an internet <b>stream</b>, for a set number of minutes;",
        "an <b>input of the studio computer</b>, live, with <b>Input…</b>: the desk's line, a satellite receiver or a remote studio.",
    ]),
    ("p", "Then choose how it starts:"),
    ("list", [
        "<b>Soft</b> waits for the item on air to end. You can let it start a few minutes <b>early</b> and set how <b>late</b> it may be at most: a window, such as the traffic report between 8:05 and 8:20, that takes the first song end in it. If no song ends in time, what is on air fades out at the end of the window. A window never opens before its own hour starts, so a 10:00 break stays in the 10 o'clock hour.",
        "<b>Hard</b> starts on the second, and what is on air fades out.",
        "<b>Ends at its time</b> finishes exactly at the time you give, never later. Use it for whatever has to end just before the news.",
    ]),
    ("p", "With <b>Ends at its time</b>, the songs before the event are adjusted as the hour goes on. If the music would still "
          "run over, what is on air fades out. If it would arrive early, a short gap is filled from the <b>Fillers</b> "
          "category. An event that ends at 10:00 is the last thing in the 09:00 hour. Any song that can no longer start before "
          "it shows <b>WON'T PLAY</b> in the playlist."),
    ("p", "Finally, choose what the presenter may do with the event in the playlist: remove and move it, only move it, or nothing."),
    ("h3", "A series: a different file on some days"),
    ("p", "Sometimes one slot plays something different each day. A horoscope at 08:15 has a recording for each day of the week, "
          "and a weekly programme has a new episode on its day. Rather than making seven events, you give one event a "
          "<b>series</b>."),
    ("fig", "schedule-series", "An event with a series", [
        ("EventList", "Select the event, here the Horoscope at 08:15 every day."),
        ("EventSeriesDays", "A file or folder for <b>each day of the week</b>: Sunday's horoscope, Monday's, and so on."),
        ("EventSeriesDates", "A file for a <b>given date</b>: an episode on its day, or a special for a holiday. Leave a date's file empty and nothing plays that day."),
        ("EventSeriesDateButton", "Pick a date and click <b>Add a date</b>. <b>Remove past dates</b>, next to it, clears dates that have gone by."),
    ]),
    ("p", "On any day, Sintoniza looks first for that date's file, then for that weekday's, and finally plays the event's own "
          "file. If there is none of these, the event is skipped that day and the log says so."),
    ("p", "A date left empty is how you take a day off: add 25 December with no file, and there is no horoscope on Christmas Day."),
    ("h2", "Shows"),
    ("p", "A show is a live programme with a presenter, such as the morning show from 7 to 10. Setting it up in <b>Shows</b> "
          "lets the playout get out of the presenter's way while they are on, and lets listeners see who is on air."),
    ("p", "Give a show its name, its presenter, how long it lasts and notes for the presenter, such as the running order, "
          "guests or what to mention. Then click the times it starts on the week calendar, as you do for events."),
    ("p", "While the show is on, the studio switches to ASSISTED and opens the presenter view, and the show and presenter go "
          "out on RDS and the website. When it ends, the playout goes back to AUTO. Chapter 3 describes this from the "
          "presenter's side."),
    ("h2", "Ad breaks"),
    ("p", "An ad break is a slot for adverts, such as every hour at :50. You set up the break once, and the adverts booked for "
          "each day fill it."),
    ("fig", "schedule-breaks", "Ad breaks", [
        ("BreakList", "The breaks."),
        ("BreakGrid", "Where they are in the week, here every hour at :50."),
        ("PartList", "The <b>running order</b>: jingles and <b>sections</b>. The adverts booked in a section play in its place."),
        ("SaveButton", "<b>Save</b>."),
    ]),
    ("p", "A jingle can be set to play only when the section after it has adverts, which makes it a section opener. A break with "
          "nothing booked is skipped. Breaks are locked in the playlist by default, so they can't be removed by accident."),
    ("p", "<b>Longest</b> sets how long a break may run, in seconds. If the adverts booked for today would make it longer, "
          "Pro's Home page warns you, so you can move some. Nothing is ever left out on air."),
    ("h2", "Clients and campaigns"),
    ("p", "This is where the commercial department books adverts. Each client, such as the local bakery, has one or more "
          "campaigns, and each campaign is booked into ad breaks."),
    ("fig", "schedule-clients", "Clients and campaigns", [
        ("ClientList", "The <b>clients</b>, with the name that appears on the proof of broadcast."),
        ("CampaignList", "Their <b>campaigns</b>. A campaign has its adverts, which take turns if there are several, its section, its position and its first and last day."),
        ("SpotGrid", "The week's ad breaks, with how many adverts each has and how long they run. <b>Click</b> a break to book the campaign there, every week or on that date only."),
        ("SaveButton", "<b>Save</b>."),
    ]),
    ("p", "<b>In every break</b> books a campaign into every break that has its section. <b>Position</b> is automatic, or 1 to 5 "
          "from the start of the section, or 6 to 10 at its end."),
    ("p", "<b>Sectors keep competitors apart.</b> Give each client a <b>sector</b>, such as Cars, Banks or Supermarkets. "
          "Sintoniza then places the adverts so that the same client, or two clients of the same sector, never follow each "
          "other where that can be avoided. No advert is ever left out for this, and Home tells you when it couldn't be avoided."),
    ("p", "<b>Certificate…</b>, under the clients, compares what was booked for a client with what actually went on air, spot "
          "by spot, for a period and one campaign or all. Each spot is <b>On air</b> with its time, <b>NOT ON AIR</b>, "
          "<b>On air, not booked</b> or <b>To come</b>. <b>Certificate to print…</b> gives you a page to print or save as PDF "
          "for the client."),
    ("p", "If Sintoniza Rec records the station, <b>▶ Listen</b> plays the selected advert exactly as it went out, and "
          "<b>Save its audio…</b> saves it as an MP3 to send to the client. Home warns you when adverts booked for yesterday "
          "didn't go on air."),
    ("p", "<b>Billing…</b> counts every client's spots over a month, last month by default, campaign by campaign: booked, on "
          "air, not on air, on air without being booked, and time on air. It counts them as the certificates do. Save it as "
          "CSV for the accounts, or print it."),
    ("h2", "Hour templates for your shows"),
    ("p", "An hour template is the recipe for an hour of music. You make one for each show or kind of hour, and the daily "
          "playlists are made from them."),
    ("fig", "schedule-templates", "Hour templates", [
        ("TemplateList", "The templates: one for each show or kind of hour."),
        ("TemplateGrid", "The hours the selected template plays: here the morning show on weekdays, 07:00 to 10:00."),
        ("LineList", "Its <b>lines</b>. They play in order, then from the top again until the hour is full."),
        ("LineCategoryBox", "A line can be a song from a <b>library category</b>, such as Music › Current, Gold or Jingles…"),
        ("AddFolderLineButton", "…or a file from a <b>folder</b>."),
        ("TempoMinBox", "The <b>tempo</b> and <b>energy</b> for the selected line. Here the first song of the morning is a lively one."),
    ]),
    ("h3", "Seasons: a summer or Christmas schedule"),
    ("p", "Many stations change their week for the summer or for Christmas. Instead of moving every template by hand, make "
          "the season's templates once and give them a season. Click <b>Seasons…</b> and add one, such as <i>Summer, from "
          "1 July to 31 August</i>. A season repeats every year, and it can run over the new year, such as 15 December to "
          "6 January."),
    ("p", "Then choose the season in a template's <b>Season</b> box, and place it on the calendar as usual. On the season's "
          "days, that template plays at its hours instead of the all-year template placed there. Every other hour keeps the "
          "all-year one. <b>Show the week</b>, above the calendar, shows the week as it will play all year or in a season."),
    ("tip", "When a season starts or ends, the daily playlists of the hours it changes are made again by themselves, unless "
            "they were changed by hand. Make the season's templates a few days ahead and check them in Daily playlists."),
    ("h2", "Music rules"),
    ("p", "Music rules shape the flow of an hour: lively songs after the news, no two slow songs in a row, the same artist "
          "kept apart. First give your songs a <b>tempo</b>, from 1 slow to 5 fast, and an <b>energy</b>, from 1 calm to 5 "
          "lively. Select them in the library, right-click and choose <i>Tempo</i> or <i>Energy</i>."),
    ("fig", "schedule-rules", "A template's music rules", [
        ("TempoMinBox", "The <b>tempo</b> range for the selected line. <i>Any</i> means no limit."),
        ("EnergyMinBox", "The <b>energy</b> range."),
        ("ArtistHoursBox", "The same <b>artist</b> not again within so many hours. Zero uses the rest time in Station settings → Safety."),
        ("AlbumHoursBox", "The same <b>album</b> not again within so many hours. Zero means no album rule."),
        ("MaxSlowBox", "At most this many <b>slow songs</b> (tempo 1 or 2) in a row. Zero means no limit."),
    ]),
    ("tip", "If a rule would leave nothing to play, it is relaxed for that one song: first the album rule, then slow songs, then "
            "the artist. The hour is always full. If no song in a category has the tempo or energy a line asks for, the line "
            "plays from the whole category, and Daily playlists tells you so."),
    ("h2", "Daily playlists"),
    ("p", "The daily playlists are the hours exactly as they will play, made from the templates: every song, jingle and "
          "advert, in order, with its start time. Here you can check tomorrow's music, swap a song, or leave a note for the "
          "presenter."),
    ("fig", "schedule-daily", "Daily playlists", [
        ("DayBox", "The day, with ◀ ▶ and <b>Today</b>."),
        ("HourList", "Its 24 hours, with the template and length of each. ✎ means changed by hand; ↻ means its template has changed since."),
        ("OnAirText", "What is on air now, and how far ahead of or behind the plan the studio is."),
        ("ItemList", "The hour: songs, jingles, the ad break's adverts with their clients, and events, each with its start time and where it came from. ✓ has played, ▶ is on air, and OPTIONAL plays only if there is time."),
        ("StreamButton", "<b>Stream…</b> puts another station's stream in this hour at a set time, on the dot or after the item on air."),
        ("SegueButton", "<b>Segue…</b> shapes how the selected song runs into the next; <b>Mix editor…</b>, next to it, shows the whole hour (below)."),
        ("TagsButton", "<b>Edit tags…</b> of the selected song."),
        ("ReplaceButton", "<b>Another song</b> picks a different song from the same template line."),
        ("RemoveButton", "<b>Remove</b> takes the selected item out. ▲ ▼ move it."),
        ("NoteButton", "<b>📝 Note…</b> and <b>📝 Hour note…</b> leave a note for the presenter, such as the sponsor to mention. It is shown on air before and while the item plays."),
        ("GenerateHourButton", "<b>Make this hour again</b> from its template."),
        ("GenerateDayButton", "<b>Make the day again</b>. Hours changed by hand are kept."),
        ("Tracks", "The library. <b>Double-click</b> a song to put it after the selected item, or right-click it to listen."),
        ("ListenButton", "<b>Listen</b> to the selected item, on the Preview output only. A double-click or Space does the same."),
        ("TotalText", "The hour's total: songs and jingles, ad breaks and events, and what is optional."),
    ]),
    ("h3", "The mix editor: a whole hour at a glance"),
    ("p", "<b>Mix editor…</b> shows the selected hour on one time line, every item where it starts, so you can see and hear how "
          "the hour flows. Songs sit on two lanes, one after the other, so you can see where each starts over the end of the "
          "one before. The red line is the top of the hour, and the line above the time line tells you whether the hour "
          "ends early or runs over."),
    ("fig", "schedule-mix", "The mix editor", [
        ("Timeline", "The hour. <b>Drag</b> an item to the left to start it earlier, over the end of the one before; everything after it moves with it. A ✋ marks an item you placed by hand. The wheel scrolls and Ctrl + wheel zooms."),
        ("EndsText", "When the hour ends, and how far that is from the top of the hour."),
        ("SelectedText", "What happens at the selected item's start, in plain words."),
        ("MovePanel", "Nudge it by a second or a tenth, or put it back to the <b>Automatic</b> crossfade."),
        ("SegueButton", "<b>Segue…</b> opens the join in the segue editor. A double-click on the item does the same."),
        ("PlayButton", "Click anywhere on the time line to put the cursor, then <b>Play from the cursor</b> to hear the hour from there, through every join, as it will sound on air, on the Preview output (Space)."),
        ("ZoomAllButton", "<b>Whole hour</b> fits the hour in the window; − and + zoom out and in."),
    ]),
    ("p", "Moving an item here changes <b>this hour only</b>. The item before it plays on to its end at full level, as it does under a "
          "voice track, which is just right for a jingle over a song's outro. It never starts later than the automatic crossfade, "
          "and at most 30 seconds over the item before. Ad breaks and events from the schedule are shown, but don't move here. "
          "On air the overlap only counts over the item it was set against: if an ad break or event ends up in between, the "
          "item simply starts after it as usual, so an advert is never talked or played over."),
    ("tip", "To change how two songs always run into each other, wherever they play, use <b>Segue…</b> instead: its cue points "
            "are saved with the songs. The mix editor is for shaping one particular hour."),
    ("p", "You can work in two ways, and mix them freely."),
    ("list", [
        "<b>Build an hour song by song</b> with Another song, ▲ ▼, Remove and the library. Each change is saved at once, and the hour is marked ✎ so it is never remade by itself.",
        "<b>Or let the rules do it.</b> Today and the next days are always made, and an hour whose template changes is made again within minutes, unless it was changed by hand or is about to play.",
    ]),
    ("p", "When picking songs, Sintoniza plays the songs that have rested longest first. It keeps the same song and the same "
          "artist apart for the rest times in Station settings → Safety, and never plays the same artist twice in a row while "
          "another can play. Songs the studio adds to keep time show as <i>added on air to keep time</i>."),
    ("h2", "Voice tracks"),
    ("p", "Voice tracking lets a presenter record their links ahead of time, as if they were live. It is how a station sounds "
          "presented through the night or the weekend. Select a song in an hour and click <b>Voice track…</b>."),
    ("fig", "voicetrack", "Voice track…", [
        ("InputBox", "The <b>input</b> to record from: the studio mic, or any input of this computer."),
        ("PreRollBox", "How much of the song's ending you hear first."),
        ("StartButton", "<b>Start</b> plays the end of the song, on the Preview output."),
        ("TalkButton", "<b>Talk</b> (Space) starts recording, over the song's ending if you like. Press it again to stop."),
        ("NextButton", "<b>Next song</b> (Enter) brings in the following item under your voice, while you are still talking."),
        ("OverlapText", "After the take: how far it starts over the end of the song…"),
        ("NextText", "…and where the next song comes in. ±0.5 s nudges either one."),
        ("PlayBackButton", "<b>Play it back</b> as it will sound on air, or <b>Record again</b>."),
        ("SaveButton", "<b>Save</b> keeps it as an MP3 in the station's voice tracks folder, and in the library under <b>Takes</b>."),
    ]),
    ("fig", "schedule-voice", "The voice track in the hour", [
        ("ItemList", "It goes right after its song, marked 🗣, and starts over the song's last seconds."),
        ("VoiceButton", "<b>Voice track…</b> again, on the song or on the voice track, records it again."),
    ]),
    ("tip", "A voice track belongs to the song before it. Removing or replacing that song takes the voice track with it, "
            "moving the song moves it too, and on air it is skipped if the song can't play."),
]),

("Sintoniza Import", "import", [
    ("p", "Sintoniza Import brings a folder of music into the library, turning the folders inside it into categories. It is "
          "for whoever looks after the music, and it asks for a user with the <b>Library</b> permission."),
    ("fig", "import", "Sintoniza Import", [
        ("FolderBox", "The <b>folder</b> to import…"),
        ("ChooseButton", "…which you can pick with <b>Choose…</b>."),
        ("NameBox", "The <b>category</b> it goes into. By default it takes the folder's name…"),
        ("ParentBox", "…<b>under</b> one of the station's eight categories, Music by default, or a category under them."),
        ("KindBox", "The <b>kind</b> of any new categories. It follows the category they go under."),
        ("LevelsBox", "<b>Folders inside become sub-categories</b>: none, or one to three levels. A folder Music/80s becomes Music › 80s."),
        ("AlsoInRadio", "What to do with songs already in another category: keep that one and add this one too, move them here, or leave them alone."),
        ("PreviewButton", "<b>Preview import</b> shows what will happen before anything changes."),
        ("PlanList", "The categories it will make, marked NEW or ALREADY THERE, the files in each, how many are already in the library and how many are in another category."),
        ("ImportButton", "<b>Import</b> makes the categories, reads the songs and puts each one in its category."),
    ]),
    ("p", "Each folder stays linked to its category, so songs you put there later get the category too. The on-air program "
          "scans those folders by itself. <b>Stop</b> halts an import, and importing again finishes the job."),
    ("h2", "radio-import, on the command line"),
    ("p", "The same scan can run without a window. Technicians use it to scan every night from cron or the Task Scheduler:"),
    ("pre", "radio-import                          scan the library's folders\n"
            "radio-import /music /jingles          scan these folders\n"
            "radio-import --db /path/library.db    an SQLite library file\n"
            "radio-import --mysql user:password@server[:port]/database\n"
            "radio-import --stats                  what is in the library"),
    ("p", "Only new and changed files are read. Deleted files, and the songs of a deleted folder, are taken out of the library. "
          "A drive or share that isn't connected is left alone, so nothing is lost if a server is down."),
]),

("Sintoniza Rec", "rec", [
    ("p", "Sintoniza Rec is the station's recorder. It records everything that goes out, all day and every day, and keeps it. "
          "In Portugal the Lei da Rádio (article 39) asks for broadcasts to be recorded and kept for at least 30 days, and a "
          "recording also settles any question from a client or a listener."),
    ("p", "Rec doesn't need the library or a user, so it can run on the studio computer or on any other. On a computer of its "
          "own, it keeps recording while the studio computer is restarted."),
    ("fig", "rec", "Sintoniza Rec", [
        ("RecText", "<b>● REC</b> while it records, and for how long. It shows STOPPED in red when it isn't recording, and SILENCE when there has been no sound for a while."),
        ("SourceText", "What it records from, and how."),
        ("MeterL", "The level of what comes in."),
        ("DiskText", "The file being written, the free space, how long recordings are kept and how often a new file starts."),
        ("RecButton", "<b>Record</b> and <b>Stop</b>. Closing the window asks first, because it stops the recording."),
        ("MonitorButton", "<b>Listen live</b> to what is being recorded, on the listening output. Use headphones, never an output that goes on air."),
        ("SettingsButton", "<b>Settings</b>: the source, the files, listening and alerts."),
        ("DayBox", "The <b>recordings of a day</b>. Today goes back to today…"),
        ("FileList", "…with each file's start and end, length and size. Double-click one, or select it and…"),
        ("PlayButton", "…press <b>▶</b> to listen. The bar below moves through it, showing the time of day it was on air."),
        ("FromBox", "<b>Export a stretch</b>: choose a day, a start and an end time and a format…"),
        ("ExportButton", "…and <b>Export…</b> saves it as one file, cut to the second, joined from as many recordings as needed. If the recorder was stopped for part of that time, the gap is left out and Rec tells you."),
    ]),
    ("h2", "Where it records from"),
    ("fig", "rec-settings", "Settings → Source", [
        ("StationBox", "The station's name, shown in the window and in alerts."),
        ("InputRadio", "Record an <b>input</b> of this computer…"),
        ("InputBox", "…and which one. The best choice is the desk's programme output, wired into the sound card, because it includes the microphones. The playout's own sound has none."),
        ("StreamRadio", "Or record the station's <b>stream</b>…"),
        ("StreamBox", "…from this address. If the stream drops, Rec opens it again by itself, in a new file."),
        ("AutostartBox", "<b>Start Sintoniza Rec when I log in</b>, so it records again by itself after a power cut or a restart."),
    ]),
    ("h2", "The files"),
    ("fig", "rec-files", "Settings → Files", [
        ("FolderBox", "The <b>folder</b>. Inside it, Rec makes a folder for each day, such as 2026-10-21, with the files in it. A shared folder works too."),
        ("FormatBox", "MP3, or WAV, which is uncompressed and takes about 10 GB a day."),
        ("BitrateBox", "The MP3 quality. <b>Mono</b> takes half the space."),
        ("MinutesBox", "<b>A new file every</b> 5 to 60 minutes. Each new file starts before the old one stops, so nothing is lost in between."),
        ("KeepDaysBox", "<b>Kept for</b>: older recordings are deleted by themselves. At least 30 days."),
        ("SpaceText", "How much space a day, and the whole period, will take."),
        ("ArchiveBox", "An extra <b>archive copy</b>, in mono and low quality, in another folder such as a NAS. You can keep it for months, or for good."),
    ]),
    ("h2", "Alerts"),
    ("p", "Rec can warn you when something goes wrong, so a fault doesn't go unnoticed for days."),
    ("fig", "rec-alerts", "Settings → Alerts", [
        ("SilenceDbBox", "<b>Silence</b>: below this level for this long, or a stream that sends nothing. Rec tells you again when the sound is back."),
        ("AliveBox", "A <b>still recording</b> message once a day. A day without one means something is wrong with the computer."),
        ("TelegramTokenBox", "Alerts can go by <b>Telegram</b>…"),
        ("SmtpServerBox", "…by <b>e-mail</b>…"),
        ("WebhookBox", "…or to a <b>web address</b>, as JSON. Whatever you fill in is used."),
        ("TestAlertButton", "<b>Send a test</b> to check it works."),
    ]),
    ("p", "Rec also warns you when a recording can't be written, for example because a disk is full or a shared folder has "
          "gone away. It keeps trying with a new file. And it warns you when there is less than 2 GB free."),
]),

("How do I…", "howto", [
    ("p", "Short recipes for the jobs that come up most often."),
    ("h3", "…give a presenter their own login?"),
    ("steps", ["In Pro, go to Station → <b>Users</b> and click <b>New user</b>.", "Type their name and a password, then click <b>Presenter</b>.",
               "Click <b>Save user</b>. They can now work on air, record voice tracks in Pro and use the web remote, and nothing else."]),
    ("h3", "…set up a weekday morning show?"),
    ("steps", ["In Pro, open <b>Hour templates</b>, click <b>New</b> and name it.", "Add its lines, for example Music › Current, Gold, Jingles, Current, 80s.",
               "Drag over the hours 07 to 10, Monday to Friday, and click <b>Save</b>.", "Check the result in <b>Daily playlists</b>. The hours are made within minutes.",
               "If the show is presented live, also add it in <b>Shows</b> with its presenter, so the studio opens the presenter view."]),
    ("h3", "…play a different horoscope every morning?"),
    ("steps", ["In Pro, open <b>Events</b>, click <b>New</b>, name it Horoscope, and place it at 08:15 every day.",
               "Under <i>A series</i>, give a file for each day from Sunday to Saturday.",
               "To skip a day, such as Christmas Day, pick the date, click <b>Add a date</b> and leave its file empty. <b>Save</b>."]),
    ("h3", "…put the adverts last in the hour?"),
    ("steps", ["In Pro, open <b>Ad breaks</b>, click <b>New</b>, and give it a running order with a section.", "<b>Add row</b> <code>*:00:00</code> and click that time to place it on every day.",
               "Choose <b>Ends at its time</b>. The break then ends exactly on the hour, as the last thing in the hour before.", "Book the campaigns in <b>Clients and campaigns</b>, then <b>Save</b>."]),
    ("h3", "…keep two car dealers out of the same stretch of adverts?"),
    ("p", "In Pro → <b>Clients and campaigns</b>, give both clients the same <b>sector</b>, such as Cars, and <b>Save</b>. "
          "Sintoniza keeps them apart wherever it can."),
    ("h3", "…change a song in tomorrow's 09:00 hour?"),
    ("steps", ["In Pro, open <b>Daily playlists</b>, press ▶ to go to tomorrow, and select <b>09:00</b>.", "Select the song and click <b>Another song</b>. Or <b>Remove</b> it and double-click one in the library."]),
    ("h3", "…put a folder of songs in Gold?"),
    ("p", "Run Sintoniza Import with that folder, into the category Gold, with no sub-categories. Or, in the on-air program, "
          "go to the Folders tab, right-click the folder and choose Category → Gold."),
    ("h3", "…show the song on air on our website?"),
    ("p", "In Pro → Station settings → <b>Website</b>, tick <b>Send now playing to the station's website</b>. Fill in the "
          "address of your page and its key, or add a text to be sent by FTP. Click <b>Test</b> beside each one to check it, then <b>Save</b>."),
    ("h3", "…send the ERC last month's music?"),
    ("steps", ["In Pro, open <b>ERC quotas</b> and choose <b>Last month</b>.", "Check the quotas, and classify anything under <i>Songs to classify</i>.",
               "Click <b>Export for the ERC (XML)…</b>, then upload the file on the ERC's portal."]),
    ("h3", "…get the music report for the collecting society?"),
    ("p", "In the on-air program, click <b>Reports</b>, choose <i>Music played</i> and the month, then <b>Save as CSV…</b>."),
    ("h3", "…send an advertiser proof of broadcast?"),
    ("p", "In <b>Reports</b>, choose <i>Adverts</i>, the advertiser and the dates, then <b>Save as a page to print…</b> and "
          "print it to PDF. In Pro, <b>Certificate…</b> under the clients does the same, comparing each spot with what was booked."),
]),

("When something isn't right", "trouble", [
    ("p", "The most common problems, and what to do about them."),
    ("table", ["What you see", "What to do"], [
        ["The on-air playlist doesn't match Daily playlists", "Press <b>Clear</b> in the on-air program. It fills again from the plan, starting with the item planned for now. If the studio runs ahead or behind, Daily playlists shows by how much; it catches up by the next hour."],
        ["Only jingles and adverts play", "The songs' files were moved or deleted. Fix the categories' folders in Pro → Categories, then click <b>Scan</b> in the Library tab."],
        ["A song is skipped with “FileOpen”", "The file isn't where the library says it is. <b>Scan</b> again."],
        ["Listening is heard on air", "In Settings → Audio, set <b>Preview</b> to an output that isn't on air, such as headphones or the desk's PFL."],
        ["A status lamp is red", "Point at it to read what is wrong, and click it to go straight to its settings."],
        ["The website doesn't change", "Look at the WEBSITE lamp. In Pro → Station settings → Website, click <b>Test</b> on the page and on each text, and read what it says. Remember that songs marked <i>Not on the website</i> are never sent."],
        ["Something went wrong on air", "In Settings → Safety, click <b>Open log folder</b>: there is one file a day. Pro → Station settings → Alerts can warn you by Telegram or e-mail next time."],
        ["A Windows or Mac studio doesn't find the songs", "In Settings → Library → <b>This computer's paths</b>, say where the library's music is on that computer. <b>Test</b> tells you how many songs it finds."],
        ["The on-air program shows STANDBY", "Another computer is on air, and the bar says which. Press <b>Take on air</b> if this one should be."],
    ]),
]),
]
