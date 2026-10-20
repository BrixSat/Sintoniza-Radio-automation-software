using System.Security.Cryptography;
using System.Text;
using Dapper;

namespace Playout.Library;

/// <summary>What a user may do (kept as these keys in users.permissions).</summary>
public static class Permission
{
    /// <summary>Songs, categories, importing music.</summary>
    public const string Library = "library";
    /// <summary>Hour templates, daily playlists (changing them), events.</summary>
    public const string Programming = "programming";
    /// <summary>Recording voice tracks (and seeing the daily playlists).</summary>
    public const string Voice = "voice";
    /// <summary>Ad breaks, clients and campaigns.</summary>
    public const string Traffic = "traffic";
    /// <summary>The station's settings and cartwalls.</summary>
    public const string Station = "station";
    /// <summary>Adding and changing users.</summary>
    public const string Users = "users";
    /// <summary>A playout's own settings (sound cards, database, keys…).</summary>
    public const string Computer = "computer";
    /// <summary>The web remote.</summary>
    public const string Remote = "remote";

    public static readonly string[] All = [Library, Programming, Voice, Traffic, Station, Users, Computer, Remote];
    /// <summary>What dj gets the first time: a presenter.</summary>
    public static readonly string[] Presenter = [Voice, Remote];
}

/// <summary>A user (the password is only ever kept as a hash).</summary>
public sealed class SintonizaUser
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public HashSet<string> Permissions { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public DateTime? LastLogin { get; set; }

    public bool Can(string permission) => Permissions.Contains(permission);
}

/// <summary>
/// The station's users, in the library database: checked by Sintoniza Pro, Sintoniza Import, the playout's Settings
/// and the web remote. Passwords are salted PBKDF2-SHA256 hashes.
/// </summary>
public sealed class UserStore(LibraryDb db)
{
    const int Iterations = 100_000;

    sealed class Row
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string Permissions { get; set; } = "";
        public long Enabled { get; set; }
        public string? LastLogin { get; set; }
    }

    static SintonizaUser From(Row r) => new()
    {
        Id = r.Id, Name = r.Name, Enabled = r.Enabled != 0,
        Permissions = r.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(),
        LastLogin = DateTime.TryParse(r.LastLogin, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var at) ? at : null,
    };

    const string Columns = "id AS Id, name AS Name, password_hash AS PasswordHash, permissions AS Permissions, enabled AS Enabled, last_login AS LastLogin";

    public List<SintonizaUser> All()
    {
        using var c = db.Connect();
        return c.Query<Row>($"SELECT {Columns} FROM users ORDER BY name").Select(From).ToList();
    }

    /// <summary>The first time: admin (password admin, everything) and dj (password dj, a presenter).</summary>
    public void EnsureDefaults()
    {
        using var c = db.Connect();
        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM users") > 0) return;
        Add(c, "admin", "admin", Permission.All);
        Add(c, "dj", "dj", Permission.Presenter);
    }

    static void Add(System.Data.Common.DbConnection c, string name, string password, IEnumerable<string> permissions) =>
        c.Execute("INSERT INTO users (name, password_hash, permissions, enabled, created_at) VALUES (@name, @hash, @permissions, 1, @now)",
            new { name, hash = Hash(password), permissions = string.Join(',', permissions), now = LibraryDb.Now() });

    /// <summary>The user, if the name and password are right and it may log in; its last login is noted.</summary>
    public SintonizaUser? LogIn(string name, string password)
    {
        using var c = db.Connect();
        var row = c.QueryFirstOrDefault<Row>($"SELECT {Columns} FROM users WHERE LOWER(name) = LOWER(@name)", new { name = name.Trim() });
        if (row == null || row.Enabled == 0 || !Verify(password, row.PasswordHash)) return null;
        c.Execute("UPDATE users SET last_login = @now WHERE id = @Id", new { now = LibraryDb.Now(), row.Id });
        return From(row);
    }

    /// <summary>Users still with their first password (admin/admin, dj/dj): worth changing.</summary>
    public List<string> WithDefaultPasswords()
    {
        using var c = db.Connect();
        return c.Query<Row>($"SELECT {Columns} FROM users WHERE name IN ('admin', 'dj') AND enabled = 1").Where(r => Verify(r.Name, r.PasswordHash)).Select(r => r.Name).ToList();
    }

    /// <summary>Adds or changes a user; <paramref name="password"/> null keeps the one it has. Returns its id.</summary>
    public long Save(SintonizaUser user, string? password)
    {
        if (string.IsNullOrWhiteSpace(user.Name)) throw new InvalidOperationException("A user needs a name.");
        using var c = db.Connect();
        string permissions = string.Join(',', user.Permissions.Where(Permission.All.Contains));
        if (user.Id == 0)
        {
            if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("A new user needs a password.");
            Add(c, user.Name.Trim(), password, user.Permissions);
            return c.ExecuteScalar<long>("SELECT id FROM users WHERE name = @name", new { name = user.Name.Trim() });
        }
        c.Execute("UPDATE users SET name = @name, permissions = @permissions, enabled = @enabled WHERE id = @Id",
            new { name = user.Name.Trim(), permissions, enabled = user.Enabled ? 1 : 0, user.Id });
        if (!string.IsNullOrEmpty(password)) c.Execute("UPDATE users SET password_hash = @hash WHERE id = @Id", new { hash = Hash(password), user.Id });
        return user.Id;
    }

    public void Delete(long id)
    {
        using var c = db.Connect();
        c.Execute("DELETE FROM users WHERE id = @id", new { id });
    }

    /// <summary>"iterations$salt$hash" (base64).</summary>
    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 3 || !int.TryParse(parts[0], out int iterations)) return false;
        try
        {
            byte[] salt = Convert.FromBase64String(parts[1]), expected = Convert.FromBase64String(parts[2]);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(hash, expected);
        }
        catch (FormatException) { return false; }
    }
}
