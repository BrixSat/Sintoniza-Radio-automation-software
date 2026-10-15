using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Playout.Engine;

namespace Playout.App;

/// <summary>Edits the tags (ID3 for MP3) and cover picture of one file. Returns true when they were saved.</summary>
public partial class TagEditorWindow : Window
{
    static readonly string[] PicturePatterns = ["*.jpg", "*.jpeg", "*.png"];

    readonly PlayoutEngine _engine = null!;
    readonly string _path = "";
    byte[]? _cover;          // the picture shown
    bool _coverChanged;      // chosen or removed here: written on Save

    // Parameterless constructor for the XAML previewer.
    public TagEditorWindow() => InitializeComponent();

    public TagEditorWindow(PlayoutEngine engine, string path)
    {
        InitializeComponent();
        Localization.Apply(this);
        _engine = engine;
        _path = path;
        Title = L.F("Edit tags — {0}", Path.GetFileName(path));
        FileText.Text = Path.GetFileName(path);
        PathText.Text = path;

        ChooseCoverButton.Click += async (_, _) => await ChooseCover();
        RemoveCoverButton.Click += (_, _) => SetCover(null, changed: true);
        CoverFrame.AddHandler(DragDrop.DragOverEvent, (_, e) =>
            e.DragEffects = DroppedPicture(e) != null ? DragDropEffects.Copy : DragDropEffects.None);
        CoverFrame.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (DroppedPicture(e) is { } file) LoadPicture(file);
        });
        CancelButton.Click += (_, _) => Close(false);
        SaveButton.Click += async (_, _) => await Save();
        Opened += async (_, _) => await Load();
    }

    async Task Load()
    {
        try
        {
            var (tags, cover) = await Task.Run(() => (Tags.Read(_path), Tags.ReadCover(_path)));
            if (tags == null)
            {
                ShowError(L.T("This kind of file has no tags that can be edited."));
                return;
            }
            TitleBox.Text = tags.Title;
            ArtistBox.Text = tags.Artist;
            AlbumBox.Text = tags.Album;
            GenreBox.Text = tags.Genre;
            YearBox.Text = tags.Year > 0 ? tags.Year.ToString(CultureInfo.InvariantCulture) : "";
            TrackBox.Text = tags.TrackNumber > 0 ? tags.TrackNumber.ToString(CultureInfo.InvariantCulture) : "";
            CommentBox.Text = tags.Comment;
            SetCover(cover, changed: false);
            SaveButton.IsEnabled = true;
            TitleBox.Focus();
        }
        catch (Exception ex)
        {
            ShowError(L.F("Cannot read the tags: {0}", ex.Message));
        }
    }

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
        SaveButton.IsEnabled = false;
        ErrorText.IsVisible = false;
        try
        {
            await _engine.SaveTagsAsync(_path, tags, _coverChanged ? _cover ?? [] : null);
            Close(true);
        }
        catch (Exception ex)
        {
            ShowError(L.F("Not saved: {0}", ex.Message));
            SaveButton.IsEnabled = true;
        }
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
