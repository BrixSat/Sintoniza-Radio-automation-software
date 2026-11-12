using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// Library → Auto cue points…: the selected songs' Cue In, Mix and Cue Out made from their sound (as auto cue does on
/// air), saved as cue points set by hand, so every studio uses them whether the station has auto cue on or not. Songs
/// with cue points set by hand keep them unless asked; Intro and Outro always stay. <see cref="Saved"/>: how many
/// songs got new ones.
/// </summary>
public sealed class AutoCueWindow : Window
{
    readonly IReadOnlyList<string> _paths;
    readonly Func<string, CuePoints> _getCues;
    readonly Func<string, CuePoints, Task> _saveCues;
    readonly WaveformLoader _waves = new(); // its own: the editors' waveforms stay in theirs
    readonly CheckBox _replace;
    readonly ProgressBar _progress;
    readonly TextBlock _status;
    readonly Button _start, _close;
    bool _running, _stop, _closeWhenDone;
    int _next, _done, _kept, _nothing;
    readonly List<string> _failed = new();

    /// <summary>How many songs got new cue points.</summary>
    public int Saved { get; private set; }

    /// <param name="getCues">A file's cue points now (empty if none).</param>
    /// <param name="saveCues">Saves a file's cue points as set by hand (from any thread).</param>
    public AutoCueWindow(IReadOnlyList<string> paths, Func<string, CuePoints> getCues, Func<string, CuePoints, Task> saveCues)
    {
        _paths = paths;
        _getCues = getCues;
        _saveCues = saveCues;
        Title = paths.Count == 1 ? L.T("Auto cue points") : L.F("Auto cue points — {0} songs", paths.Count);
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _replace = new CheckBox { Content = L.T("Also songs whose cue points were set by hand (their Intro and Outro stay)") };
        _progress = new ProgressBar { Minimum = 0, Maximum = paths.Count, Height = 6, IsVisible = false };
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        _start = new Button { Content = L.T("Make cue points"), IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        _start.Classes.Add("accent");
        _close = new Button { Content = L.T("Close"), IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        _start.Click += async (_, _) => await Run();
        _close.Click += (_, _) =>
        {
            if (!_running) { Close(); return; }
            _stop = true;
            _close.IsEnabled = false; // until the songs being looked at are done
        };
        Closing += (_, e) =>
        {
            if (!_running) return;
            // Closed while it runs: the songs being looked at are the last, and it closes once they are saved (so
            // what opened it sees every save, and no editor opens on a song still being saved).
            _stop = _closeWhenDone = true;
            e.Cancel = true;
            _close.IsEnabled = false;
        };

        Content = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = paths.Count == 1 ? L.T("Make this song's cue points from its sound?") : L.F("Make the cue points of {0} songs from their sound?", paths.Count),
                        FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = L.T("Cue In past the silence at the start, Cue Out before the silence at the end, and Mix where a fade-out has gone quiet. They are saved as cue points set by hand: every studio uses them, also with auto cue off, and they can be edited in the cue editor."),
                        TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Muted(),
                    },
                    _replace,
                    _progress,
                    _status,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { _close, _start } },
                },
            },
        };
    }

    static IBrush? Muted() => Application.Current?.TryGetResource("TextMuted", null, out var brush) == true ? brush as IBrush : null;

    async Task Run()
    {
        _running = true;
        _stop = false;
        _start.IsEnabled = _replace.IsEnabled = false;
        _close.Content = L.T("Stop");
        _progress.IsVisible = true;
        _next = _done = _kept = _nothing = Saved = 0;
        _failed.Clear();
        _progress.Value = 0;
        // A couple at a time where the computer has cores to spare; BASS needs its "no sound" device to read them.
        int workers = Math.Clamp(Environment.ProcessorCount / 4, 1, 3);
        // No file: only BASS started (a file on a share that doesn't answer could hang here).
        await Task.Run(() => AudioProbe.Duration(""));
        await Task.WhenAll(Enumerable.Range(0, workers).Select(_ => Worker()));
        _running = false;
        _close.IsEnabled = true;
        if (_closeWhenDone)
        {
            Close();
            return;
        }
        _close.Content = L.T("Close");
        _start.Content = L.T("Make them again");
        _start.IsEnabled = _replace.IsEnabled = true;
        ShowSummary();
    }

    /// <summary>Takes the next song until there are none (on the UI thread between songs, so the counters need no lock).</summary>
    async Task Worker()
    {
        while (!_stop && _next < _paths.Count)
        {
            string path = _paths[_next++];
            _status.Text = L.F("{0} of {1}: {2}", _next, _paths.Count, Path.GetFileName(path));
            try
            {
                switch (await One(path, _replace.IsChecked == true))
                {
                    case Outcome.Saved: Saved++; break;
                    case Outcome.Kept: _kept++; break;
                    case Outcome.Nothing: _nothing++; break;
                }
            }
            catch (Exception ex)
            {
                _failed.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
            _done++;
            _progress.Value = _done;
        }
    }

    enum Outcome { Saved, Kept, Nothing }

    async Task<Outcome> One(string path, bool replace)
    {
        var current = await Task.Run(() => _getCues(path));
        // Auto cue's own (where the station uses them) are made again; set by hand only when asked.
        var own = current.Auto ? CuePoints.None : current;
        if (own.HasValues && !replace) return Outcome.Kept;
        if (!SharedPaths.FileExists(path)) throw new FileNotFoundException(L.T("The file isn't there"));
        var load = _waves.LoadAsync(path);
        // A file on a share that doesn't answer: passed over after a minute.
        bool read = await Task.WhenAny(load, Task.Delay(TimeSpan.FromMinutes(1))) == load;
        _waves.Forget(path);
        if (!read) throw new TimeoutException(L.T("The file couldn't be read within a minute"));
        if (load.Result is not { Peak.Length: > 0 } wave) throw new InvalidOperationException(L.T("Its sound couldn't be read"));
        var found = CueDetector.Auto(wave);
        // What the sound shows nothing to cut for keeps the song's own (a Cue In past a count-in, a Mix on a hard ending).
        double? cueIn = found.CueIn ?? own.CueIn, cueOut = found.CueOut ?? own.CueOut;
        double? mix = found.Mix ?? own.Mix;
        if (mix is { } m && (m < (cueIn ?? 0) || cueOut is { } o && m > o)) mix = null; // the old Mix outside the new cut
        // Intro and Outro stay, inside the new Cue In and Cue Out.
        bool Inside(double? t) => t is not { } v || v >= (cueIn ?? 0) && (cueOut is not { } end || v <= end);
        var cues = own with
        {
            CueIn = cueIn, Mix = mix, CueOut = cueOut, Auto = false, FileSize = null,
            Intro = Inside(own.Intro) ? own.Intro : null, Outro = Inside(own.Outro) ? own.Outro : null,
        };
        if (cues == own) return Outcome.Nothing; // nothing to cut (and nothing to change)
        await _saveCues(path, cues);
        return Outcome.Saved;
    }

    void ShowSummary()
    {
        var parts = new List<string> { L.F("New cue points: {0}", Saved) };
        if (_nothing > 0) parts.Add(L.F("nothing to cut: {0}", _nothing));
        if (_kept > 0) parts.Add(L.F("kept their own: {0}", _kept));
        if (_failed.Count > 0) parts.Add(L.F("couldn't be read: {0}", _failed.Count));
        if (_done < _paths.Count) parts.Add(L.F("not looked at (stopped): {0}", _paths.Count - _done));
        string text = string.Join(" · ", parts);
        if (_failed.Count > 0)
            text += "\n" + string.Join("\n", _failed.Take(6)) + (_failed.Count > 6 ? "\n" + L.F("…and {0} more", _failed.Count - 6) : "");
        _status.Text = text;
    }
}
