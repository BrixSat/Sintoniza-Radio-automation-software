using System.Data.Common;
using Dapper;

namespace Playout.Library;

/// <summary>
/// A top-level category every station has: it can't be deleted, renamed or moved. A station adds its own categories
/// under these, or beside them at the top.
/// </summary>
/// <param name="Role">Kept in categories.role.</param>
/// <param name="Kind">music | jingle | commercial | other: how what it holds is played and reported.</param>
/// <param name="Aliases">Top-level categories taken in as this one (names compared without case or accents).</param>
public sealed record FixedCategory(string Role, string Name, string Kind, string Color, string Help, string[] Aliases);

/// <summary>The station's eight top-level categories (the same names in every language).</summary>
public static class FixedCategories
{
    public static readonly IReadOnlyList<FixedCategory> All =
    [
        new("music", "Music", "music", "#3E8EDE", "Songs", ["music", "musica", "musicas", "musique", "songs"]),
        new("jingles", "Jingles", "jingle", "#B4232A", "Station jingles and IDs", ["jingles", "jingle"]),
        new("promos", "Promos", "jingle", "#7B5CC4", "Promotions of the station and its shows", ["promos", "promo", "promocoes"]),
        new("progautor", "ProgAutor", "other", "#5B6274", "Shows recorded outside", ["progautor", "programas", "programs", "shows"]),
        new("fillers", "Fillers", "music", "#2FA37A", "Short fills: beds and instrumentals", ["fillers", "filler", "enchimento"]),
        new("redacao", "Redação", "other", "#0097A7", "News", ["redacao", "news", "noticias", "noticiario"]),
        new("spots", "Spots", "commercial", "#1F6F4A", "Adverts", ["spots", "ads", "adverts", "commercials", "publicidade", "anuncios"]),
        new("takes", "Takes", "jingle", "#D6A21E", "Voice inserts: the time, the temperature… as if a host were there", ["takes", "voice", "inserts"]),
    ];

    public static FixedCategory? ByRole(string? role) => All.FirstOrDefault(f => f.Role == role);

    /// <summary>Where a top-level category of this kind goes when it isn't one of the eight.</summary>
    static string HomeFor(string kind) => kind switch
    {
        "jingle" => "jingles",
        "commercial" => "spots",
        "other" => "progautor",
        _ => "music",
    };

    /// <summary>
    /// Makes the eight top-level categories (taking in top-level ones of the same name), keeps their names, kinds and
    /// order, and puts every other top-level category under the one for its kind. Songs keep their categories.
    /// </summary>
    internal static void Ensure(DbConnection c, Func<string, string> normalize, bool mySql)
    {
        using var tx = c.BeginTransaction();
        var rows = c.Query<(long Id, long? ParentId, string Name, string Kind, string? Color, string? Role)>(
            "SELECT id, parent_id, name, kind, color, role FROM categories", transaction: tx).ToList();
        string Key(string name) => normalize(name).Trim();
        // The first time (a library from before the fixed categories): its top-level ones go under them. After that,
        // top-level categories of the station's own stay where they are.
        bool firstTime = !rows.Any(r => r.Role != null);
        var ids = new Dictionary<string, long>();
        int order = 1;
        foreach (var f in All)
        {
            long id;
            if (rows.FirstOrDefault(r => r.Role == f.Role) is { Id: > 0 } byRole) id = byRole.Id;
            else if (rows.FirstOrDefault(r => r.ParentId == null && r.Role == null && f.Aliases.Contains(Key(r.Name))) is { Id: > 0 } byName)
            {
                id = byName.Id;
                rows.Remove(byName);
                rows.Add(byName with { Role = f.Role });
            }
            else
            {
                // A top-level category already has the name (but isn't this one): it moves under it later, renamed.
                var clash = rows.FirstOrDefault(r => r.ParentId == null && r.Role == null && Key(r.Name) == Key(f.Name));
                if (clash.Id > 0) c.Execute("UPDATE categories SET name = @name WHERE id = @Id", new { name = clash.Name + " (old)", clash.Id }, tx);
                id = c.ExecuteScalar<long>("INSERT INTO categories (parent_id, name, kind, color, sort_order, role) VALUES (NULL, @Name, @Kind, @Color, 0, @Role); "
                                           + (mySql ? "SELECT LAST_INSERT_ID();" : "SELECT last_insert_rowid();"), f, tx);
                rows.Add((id, null, f.Name, f.Kind, f.Color, f.Role));
            }
            c.Execute("UPDATE categories SET parent_id = NULL, name = @Name, kind = @Kind, role = @Role, sort_order = @order, color = COALESCE(color, @Color) WHERE id = @id",
                new { f.Name, f.Kind, f.Role, f.Color, order = order++, id }, tx);
            ids[f.Role] = id;
        }
        // The other top-level categories go under the one for their kind; a name taken there gets a number.
        foreach (var r in rows.Where(r => firstTime && r.ParentId == null && r.Role == null))
        {
            long home = ids[HomeFor(r.Kind)];
            var siblings = rows.Where(x => x.ParentId == home).Select(x => Key(x.Name)).ToHashSet();
            string name = r.Name;
            for (int n = 2; siblings.Contains(Key(name)); n++) name = $"{r.Name} ({n})";
            c.Execute("UPDATE categories SET parent_id = @home, name = @name WHERE id = @Id", new { home, name, r.Id }, tx);
        }
        tx.Commit();
    }
}
