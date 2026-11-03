using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// Sintoniza Pro starting again on another station keeps who was logged in: the copy closing leaves a one-time code
/// in this computer's settings folder (only its user can read it) and gives it to the new copy on its command line;
/// the new copy takes it within a minute, once, and the file is gone. The code alone, or the file alone, logs no one in.
/// </summary>
public static class LoginHandoff
{
    public const string Arg = "--login=";
    static readonly TimeSpan Valid = TimeSpan.FromSeconds(60);

    sealed record Ticket(long UserId, string Hash, DateTime AtUtc);

    static string FilePath => Path.Combine(AppSettings.DataFolder, "pro-login.json");

    /// <summary>Leaves the code for this user; the argument that gives it to the new copy.</summary>
    public static string Give(SintonizaUser user)
    {
        string code = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var ticket = new Ticket(user.Id, Hash(code), DateTime.UtcNow);
        Directory.CreateDirectory(AppSettings.DataFolder);
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var file = new FileStream(FilePath, options)) JsonSerializer.Serialize(file, ticket);
        return Arg + code;
    }

    /// <summary>The user the copy that started this one left, if its code is on the command line and still good; once.</summary>
    public static SintonizaUser? Take(IEnumerable<string>? args, UserStore users)
    {
        string? code = args?.FirstOrDefault(a => a.StartsWith(Arg, StringComparison.Ordinal))?[Arg.Length..];
        if (code == null) { Forget(); return null; }
        Ticket? ticket = null;
        try
        {
            if (File.Exists(FilePath)) ticket = JsonSerializer.Deserialize<Ticket>(File.ReadAllText(FilePath));
        }
        catch (Exception) { /* not readable: the login asks */ }
        finally
        {
            try { File.Delete(FilePath); } catch (Exception) { /* gone already */ }
        }
        if (ticket == null || DateTime.UtcNow - ticket.AtUtc > Valid || ticket.AtUtc > DateTime.UtcNow.AddSeconds(5)) return null;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(code)), Encoding.ASCII.GetBytes(ticket.Hash))) return null;
        // Still a user who may log in (not locked out meanwhile).
        return users.All().FirstOrDefault(u => u.Id == ticket.UserId && u.Enabled);
    }

    /// <summary>Leaves nothing behind (the new copy didn't start, or this one started without a code).</summary>
    public static void Forget()
    {
        try { File.Delete(FilePath); } catch (Exception) { /* not there */ }
    }

    static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(code)));
}
