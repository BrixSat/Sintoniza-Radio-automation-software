using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// Edits the tags (ID3 for MP3) and cover picture of one file, or of several at once: then only the boxes changed
/// (and the cover, if chosen or removed) are written in each, the rest keeps every file's own. Returns true when
/// something was saved (<see cref="SavedPaths"/>: which files).
/// </summary>
public partial class TagEditorWindow : Window
{
    static readonly string[] PicturePatterns = ["*.jpg", "*.jpeg", "*.png"];

    readonly Func<string, TrackTags, byte[]?, Task> _save = (_, _, _) => Task.CompletedTask;
    readonly IReadOnlyList<string> _paths = [];
    bool Many => _paths.Count > 1;
    byte[]? _cover;          // the picture shown
    bool _coverChanged;      // chosen or removed here: written on Save
    readonly Dictionary<TextBox, string> _shown = new(); // several files: what each box showed (changed: written in all)
    List<string> _editable = []; // the files whose tags could be read
    bool _saving;

    /// <summary>The files whose tags were written.</summary>
    public List<string> SavedPaths { get; } = new();

    // Parameterless constructor for the XAML previewer.
    public TagEditorWindow() => InitializeComponent();

    /// <summary>In the playout: saved through the engine, which lets go of the file while it is rewritten (refused while it plays).</summary>
    public TagEditorWindow(PlayoutEngine engine, IReadOnlyList<string> paths) : this(engine.SaveTagsAsync, paths) { }

    public TagEditorWindow(Func<string, TrackTags, byte[]?, Task> save, string path) : this(save, [path]) { }

    /// <param name="save">Writes the tags (path, tags, new cover or null to keep it, empty to remove it).</param>
    public TagEditorWindow(Func<string, TrackTags, byte[]?, Task> save, IReadOnlyList<string> paths)
    {
        InitializeComponent();
        Localization.Apply(this);
        _save = save;
        _paths = paths;
        if (Many)
        {
            Title = L.F("Edit tags — {0} songs", paths.Count);
            FileText.Text = L.F("{0} songs", paths.Count);
            PathText.Text = string.Join(", ", paths.Take(5).Select(p => Path.GetFileName(p))) + (paths.Count > 5 ? ", …" : "");
            ManyText.IsVisible = true;
            // The same title or track number in every song is never what is meant.
            TitleBox.IsEnabled = TrackBox.IsEnabled = false;
            ToolTip.SetTip(TitleBox, L.T("Each song keeps its own title"));
            ToolTip.SetTip(TrackBox, L.T("Each song keeps its own track number"));
        }
        else
        {
            Title = L.F("Edit tags — {0}", Path.GetFileName(paths[0]));
            FileText.Text = Path.GetFileName(paths[0]);
            PathText.Text = paths[0];
        }

        ChooseCoverButton.Click += async (_, _) => await ChooseCover();
        RemoveCoverButton.Click += (_, _) => SetCover(null, changed: true);
        CoverFrame.AddHandler(DragDrop.DragOverEvent, (_, e) =>
            e.DragEffects = DroppedPicture(e) != null ? DragDropEffects.Copy : DragDropEffects.None);
        CoverFrame.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (DroppedPicture(e) is { } file) LoadPicture(file);
        });
        CancelButton.Click += (_, _) => Close(SavedPaths.Count > 0);
        SaveButton.Click += async (_, _) => await Save();
        Closing += (_, e) => { if (_saving) e.Cancel = true; }; // not halfway through the files
        Opened += async (_, _) => await Load();
    }

    async Task Load()
    {
        if (Many)
        {
            await LoadMany();
            return;
        }
        try
        {
            var (tags, cover) = await Task.Run(() => (Tags.Read(_paths[0]), Tags.ReadCover(_paths[0])));
            if (tags == null)
            {
                ShowError(L.T("This kind of file has no tags that can be edited."));
                return;
            }
            TitleBox.Text = tags.Title;
            ArtistBox.Text = tags.Artist;
            AlbumBox.Text = tags.Album;
            GenreBox.Text = tags.Genre;
            YearBox.Text = Number(tags.Year);
            TrackBox.Text = Number(tags.TrackNumber);
            CommentBox.Text = tags.Comment;
            SetCover(cover, changed: false);
            _editable = [_paths[0]];
            SaveButton.IsEnabled = true;
            TitleBox.Focus();
        }
        catch (Exception ex)
        {
            ShowError(L.F("Cannot read the tags: {0}", ex.Message));
        }
    }

    /// <summary>Several files: a box shows what they all have, or stays empty where they differ.</summary>
    async Task LoadMany()
    {
        ShowProgress(L.T("Reading the tags…"));
        var read = await Task.Run(() => _paths.Select(p =>
        {
            try { return (Path: p, Tags: Tags.Read(p), Cover: Tags.ReadCover(p), Error: (string?)null); }
            catch (Exception ex) { return (Path: p, Tags: (TrackTags?)null, Cover: (byte[]?)null, Error: ex.Message); }
        }).ToList());
        ProgressText.IsVisible = false;
        var ok = read.Where(r => r.Tags != null).ToList();
        _editable = ok.Select(r => r.Path).ToList();
        if (ok.Count == 0)
        {
            ShowError(L.T("None of these files has tags that can be edited."));
            return;
        }
        void Show(TextBox box, Func<TrackTags, string> value)
        {
            var values = ok.Select(r => value(r.Tags!)).Distinct().ToList();
            box.Text = values.Count == 1 ? values[0] : "";
            if (values.Count > 1) box.PlaceholderText = L.T("Different in each song");
            _shown[box] = box.Text ?? "";
        }
        Show(ArtistBox, t => t.Artist);
        Show(AlbumBox, t => t.Album);
        Show(GenreBox, t => t.Genre);
        Show(YearBox, t => Number(t.Year));
        Show(CommentBox, t => t.Comment);
        // The cover: shown if they all have the same one.
        var first = ok[0].Cover;
        if (ok.All(r => r.Cover == null ? first == null : first != null && r.Cover.AsSpan().SequenceEqual(first))) SetCover(first, changed: false);
        else
        {
            SetCover(null, changed: false);
            NoCoverText.Text = L.T("Different covers") + "\n\n" + L.T("Drop a picture here");
            RemoveCoverButton.IsEnabled = true;
        }
        if (ok.Count < read.Count)
            ShowError(L.F("{0} of the files can't be edited: {1}", read.Count - ok.Count,
                string.Join(", ", read.Where(r => r.Tags == null).Take(5).Select(r => Path.GetFileName(r.Path)))));
        SaveButton.IsEnabled = true;
        ArtistBox.Focus();
    }

    static string Number(uint n) => n > 0 ? n.ToString(CultureInfo.InvariantCulture) : "";

    /// <summary>Shows the picture (null: none). False, and nothing changes, if the data is not a picture we can show.</summary>
    bool SetCover(byte[]? data, bool changed)
    {
        Bitmap? bitmap = null;
        if (data != null)
        {
            try { bitmap = Bitmap.DecodeToWidth(new MemoryStream(data), 360); }
            catch { return false; }
        }
        (CoverImage.Source as IDisposable)?.Dispose();
        CoverImage.Source = bitmap;
        NoCoverText.IsVisible = bitmap == null;
        RemoveCoverButton.IsEnabled = bitmap != null;
        if (changed) NoCoverText.Text = L.T("No cover\n\nDrop a picture here");
        _cover = data;
        _coverChanged |= changed;
        return true;
    }

    async Task ChooseCover()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("Choose cover picture"),
            FileTypeFilter = [new FilePickerFileType(L.T("Pictures (JPEG, PNG)")) { Patterns = PicturePatterns }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) LoadPicture(path);
    }

    static string? DroppedPicture(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>()
            .FirstOrDefault(p => PicturePatterns.Any(pattern => p.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase)));

    void LoadPicture(string path)
    {
        try
        {
            if (SetCover(File.ReadAllBytes(path), changed: true)) ErrorText.IsVisible = false;
            else ShowError(L.F("{0} is not a picture that can be used.", Path.GetFileName(path)));
        }
        catch (Exception ex)
        {
            ShowError(L.F("Cannot read {0}: {1}", Path.GetFileName(path), ex.Message));
        }
    }

    async Task Save()
    {
        if (!TryNumber(YearBox.Text, 9999, out uint year))
        {
            ShowError(L.T("Enter the year as a number, e.g. 1998, or leave it empty."));
            return;
        }
        if (!TryNumber(TrackBox.Text, 9999, out uint track))
        {
            ShowError(L.T("Enter the track number as a number, or leave it empty."));
            return;
        }

        var tags = new TrackTags(TitleBox.Text ?? "", ArtistBox.Text ?? "", AlbumBox.Text ?? "", GenreBox.Text ?? "",
            year, track, CommentBox.Text ?? "");
        byte[]? cover = _coverChanged ? _cover ?? [] : null;
        SaveButton.IsEnabled = false;
        ErrorText.IsVisible = false;
        if (Many)
        {
            await SaveMany(tags, cover);
            return;
        }
        try
        {
            await _save(_paths[0], tags, cover);
            SavedPaths.Add(_paths[0]);
            Close(true);
        }
        catch (Exception ex)
        {
            ShowError(L.F("Not saved: {0}", ex.Message));
            SaveButton.IsEnabled = true;
        }
    }

    /// <summary>Several files: in each, the boxes changed replace its own; one that can't be written doesn't stop the rest.</summary>
    async Task SaveMany(TrackTags typed, byte[]? cover)
    {
        bool Changed(TextBox box) => (box.Text ?? "") != _shown.GetValueOrDefault(box, "");
        bool artist = Changed(ArtistBox), album = Changed(AlbumBox), genre = Changed(GenreBox), year = Changed(YearBox), comment = Changed(CommentBox);
        if (!artist && !album && !genre && !year && !comment && cover == null)
        {
            Close(SavedPaths.Count > 0); // nothing changed
            return;
        }
        var failed = new List<string>();
        _saving = true;
        CancelButton.IsEnabled = false;
        try
        {
            for (int i = 0; i < _editable.Count; i++)
            {
                string path = _editable[i];
                ShowProgress(L.F("Saving {0} of {1}: {2}", i + 1, _editable.Count, Path.GetFileName(path)));
                try
                {
                    // Read again: what the file has now, with the changed boxes over it.
                    var own = await Task.Run(() => Tags.Read(path)) ?? throw new InvalidOperationException(L.T("This kind of file has no tags that can be edited."));
                    var tags = own with
                    {
                        Artist = artist ? typed.Artist : own.Artist,
                        Album = album ? typed.Album : own.Album,
                        Genre = genre ? typed.Genre : own.Genre,
                        Year = year ? typed.Year : own.Year,
                        Comment = comment ? typed.Comment : own.Comment,
                    };
                    await _save(path, tags, cover);
                    SavedPaths.Add(path);
                }
                catch (Exception ex)
                {
                    failed.Add($"{Path.GetFileName(path)}: {ex.Message}");
                }
            }
        }
        finally
        {
            _saving = false;
            CancelButton.IsEnabled = true;
            ProgressText.IsVisible = false;
        }
        if (failed.Count == 0)
        {
            Close(true);
            return;
        }
        // Those saved stay saved; the window stays open saying which weren't.
        ShowError(L.F("Saved in {0} of {1} songs. Not saved:", SavedPaths.Count, _editable.Count) + "\n" + string.Join("\n", failed.Take(8))
            + (failed.Count > 8 ? "\n" + L.F("…and {0} more", failed.Count - 8) : ""));
        _editable = _editable.Where(p => !SavedPaths.Contains(p)).ToList(); // Save again: only those left
        CancelButton.Content = L.T("Close");
        SaveButton.IsEnabled = true;
    }

    void ShowProgress(string message)
    {
        ProgressText.Text = message;
        ProgressText.IsVisible = true;
    }

    static bool TryNumber(string? text, uint max, out uint value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return true;
        return uint.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value <= max;
    }

    void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        (CoverImage.Source as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}
