namespace Playout.Engine;

/// <summary>The tags of an audio file that can be edited (ID3 for MP3; Vorbis comments, MP4 atoms etc. for other formats).</summary>
public sealed record TrackTags(
    string Title = "",
    string Artist = "",
    string Album = "",
    string Genre = "",
    uint Year = 0,
    uint TrackNumber = 0,
    string Comment = "");

/// <summary>What music reports (royalties) ask for beyond artist and title; empty when the file doesn't say.</summary>
public sealed record TrackDetails(string Album = "", string Composer = "", string Label = "", string Isrc = "", uint Year = 0);

/// <summary>Reads and writes tags and the cover picture of audio files (TagLib#).</summary>
public static class Tags
{
    /// <summary>The file's tags, or null if its format has no tags we can read.</summary>
    public static TrackTags? Read(string path)
    {
        using var file = Open(path);
        if (file == null) return null;
        var t = file.Tag;
        return new TrackTags(t.Title ?? "", t.FirstPerformer ?? "", t.Album ?? "", t.FirstGenre ?? "", t.Year, t.Track, t.Comment ?? "");
    }

    /// <summary>Album, composer, record label (publisher), ISRC and year; empty details if the tags can't be read.</summary>
    public static TrackDetails ReadDetails(string path)
    {
        try
        {
            using var file = Open(path);
            if (file == null) return new();
            var t = file.Tag;
            string isrc = t.ISRC?.Trim() ?? "";
            // Some programs write the ISRC as a user text frame (TXXX:ISRC) instead of TSRC.
            if (isrc.Length == 0 && file.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3
                && TagLib.Id3v2.UserTextInformationFrame.Get(id3, "ISRC", TagLib.StringType.UTF8, false, false) is { } frame)
                isrc = frame.Text.FirstOrDefault()?.Trim() ?? "";
            return new TrackDetails(t.Album?.Trim() ?? "", string.Join(" / ", t.Composers ?? []).Trim(), t.Publisher?.Trim() ?? "", isrc, t.Year);
        }
        catch
        {
            return new();
        }
    }

    /// <summary>The front cover (else the first picture) in the file, as image file bytes; null if it has none or can't be read.</summary>
    public static byte[]? ReadCover(string path)
    {
        try
        {
            using var file = Open(path);
            var pictures = file?.Tag.Pictures;
            if (pictures is not { Length: > 0 }) return null;
            var picture = pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover) ?? pictures[0];
            return picture.Data.Data is { Length: > 0 } data ? data : null;
        }
        catch
        {
            return null; // damaged tags: no cover
        }
    }

    /// <summary>
    /// Writes the tags. Cover: null keeps the current picture, an empty array removes all pictures, otherwise the
    /// image file bytes (JPEG or PNG) become the front cover. Throws with a readable message if it can't be saved.
    /// </summary>
    public static void Write(string path, TrackTags tags, byte[]? cover)
    {
        using var file = Open(path) ?? throw new NotSupportedException(L.T("This kind of file has no tags that can be edited."));
        // MP3: write ID3v2.3, which every player reads (v2.4 is not understood by some older ones). Only MP3:
        // asked for on a FLAC file, TagLib would put an ID3 tag in front of it, which FLAC tools reject.
        if (file is TagLib.Mpeg.AudioFile && file.GetTag(TagLib.TagTypes.Id3v2, true) is TagLib.Id3v2.Tag id3) id3.Version = 3;

        var t = file.Tag;
        t.Title = Empty(tags.Title);
        t.Performers = Empty(tags.Artist) is { } artist ? [artist] : [];
        t.Album = Empty(tags.Album);
        t.Genres = Empty(tags.Genre) is { } genre ? [genre] : [];
        t.Year = tags.Year;
        t.Track = tags.TrackNumber;
        t.Comment = Empty(tags.Comment);

        if (cover != null)
            t.Pictures = cover.Length == 0 ? [] :
            [
                new TagLib.Picture(new TagLib.ByteVector(cover))
                {
                    Type = TagLib.PictureType.FrontCover,
                    MimeType = IsPng(cover) ? "image/png" : "image/jpeg",
                    Description = "",
                },
            ];

        try
        {
            file.Save();
        }
        catch (UnauthorizedAccessException)
        {
            throw new IOException(L.T("The file is read-only or you don't have permission to change it."));
        }
    }

    static TagLib.File? Open(string path)
    {
        try
        {
            return TagLib.File.Create(path);
        }
        catch (TagLib.UnsupportedFormatException)
        {
            return null;
        }
        catch (TagLib.CorruptFileException ex)
        {
            throw new InvalidDataException(L.F("The file's tags are damaged ({0}).", ex.Message));
        }
    }

    static string? Empty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    static bool IsPng(byte[] data) => data is [0x89, (byte)'P', (byte)'N', (byte)'G', ..];
}
