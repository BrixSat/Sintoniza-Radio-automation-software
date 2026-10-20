namespace Playout.Engine;

/// <summary>
/// Decides whether a file is music, a jingle or a commercial.
/// First version: by the names of the folders it lives in (e.g. ".../Jingles/ID 1.mp3").
/// Meant to be replaced by category rules from the music library (folder → category mappings,
/// with a per-file tag override) once the library exists.
/// </summary>
public static class TrackClassifier
{
    static readonly string[] JingleWords = ["jingle", "jingles", "ids", "id", "sweeper", "sweepers", "stinger", "stingers", "vinheta", "vinhetas", "idents"];
    static readonly string[] CommercialWords = ["ads", "adverts", "advertising", "spot", "spots", "commercial", "commercials", "comercial", "comerciais", "publicidade", "pub"];

    public static TrackKind Classify(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir == null) return TrackKind.Music;

        // Look at the nearest folders first, so ".../Music/Jingles/x.mp3" is a jingle.
        foreach (var folder in dir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Reverse().Take(3))
        {
            var words = folder.ToLowerInvariant().Split([' ', '_', '-', '.'], StringSplitOptions.RemoveEmptyEntries);
            if (words.Any(w => CommercialWords.Contains(w))) return TrackKind.Commercial;
            if (words.Any(w => JingleWords.Contains(w))) return TrackKind.Jingle;
        }
        return TrackKind.Music;
    }
}
