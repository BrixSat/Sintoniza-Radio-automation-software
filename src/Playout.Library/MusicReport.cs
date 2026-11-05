using Dapper;

namespace Playout.Library;

/// <summary>A song of the library as the music report needs it: its file, its number and its own category.</summary>
public sealed record ReportTrack(long Id, string Path, long? CategoryId);

public sealed partial class LibraryDb
{
    /// <summary>The library's songs with these files (those it doesn't know are left out), many at once.</summary>
    public List<ReportTrack> TracksByPath(IEnumerable<string> paths)
    {
        var all = paths.Distinct(StringComparer.Ordinal).ToList();
        var found = new List<ReportTrack>();
        using var c = Connect();
        for (int i = 0; i < all.Count; i += 500)
            found.AddRange(c.Query<(long Id, string Path, long? CategoryId)>(
                    "SELECT id, path, category_id FROM tracks WHERE path IN @paths", new { paths = all.Skip(i).Take(500).ToArray() })
                .Select(r => new ReportTrack(r.Id, r.Path, r.CategoryId)));
        return found;
    }
}
