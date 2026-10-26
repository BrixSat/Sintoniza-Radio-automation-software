using System.Net.Sockets;
using MySqlConnector;
using Playout.Library;

namespace Playout.App;

/// <summary>Why a MySQL / MariaDB library can't be opened, in words that say what to change (on the server, most often).</summary>
static class DbErrors
{
    public static string Explain(Exception ex, LibrarySettings settings)
    {
        if (!settings.IsMySql) return ex.Message;
        var mysql = ex as MySqlException ?? ex.InnerException as MySqlException;
        bool here = settings.Server is "localhost" or "127.0.0.1" or "::1";
        string hint = mysql?.ErrorCode switch
        {
            MySqlErrorCode.UnableToConnectToHost when !here && !Resolves(settings.Server) => L.F("There is no server called {0}: check its name or address.", settings.Server),
            MySqlErrorCode.UnableToConnectToHost when !here => L.F(
                "The server {0} doesn't answer on port {1}. On the database server: MariaDB / MySQL listens only on 127.0.0.1 by default — set bind-address = 0.0.0.0 (Debian, Ubuntu: /etc/mysql/mariadb.conf.d/50-server.cnf) and restart it; and let port {1} through its firewall.",
                settings.Server, settings.Port),
            MySqlErrorCode.UnableToConnectToHost => L.F("Nothing answers on port {0} of this computer: is MariaDB / MySQL running?", settings.Port),
            MySqlErrorCode.AccessDenied when !here => L.F(
                "The server refused user {0} from this computer. A user made for 'localhost' only works on the server itself: on the server, create it for this computer or for any ('%'), e.g. CREATE USER '{0}'@'%' IDENTIFIED BY '…'; GRANT ALL PRIVILEGES ON {1}.* TO '{0}'@'%';",
                settings.User, settings.Database),
            MySqlErrorCode.AccessDenied => L.F("The server refused user {0}: check the user and password.", settings.User),
            MySqlErrorCode.DatabaseAccessDenied => L.F("User {0} can't use the database {1}: it doesn't exist, or the user has no rights on it (GRANT ALL PRIVILEGES ON {1}.* TO …).", settings.User, settings.Database),
            MySqlErrorCode.UnknownDatabase => L.F("There is no database {0} on the server: create it (CREATE DATABASE {0} CHARACTER SET utf8mb4;).", settings.Database),
            _ when ex is SocketException || ex.InnerException is SocketException => L.F("The server {0} can't be reached: check its name or address.", settings.Server),
            _ => "",
        };
        return hint.Length > 0 ? $"{ex.Message} — {hint}" : ex.Message;
    }

    static bool Resolves(string host)
    {
        try { return System.Net.IPAddress.TryParse(host, out _) || System.Net.Dns.GetHostAddresses(host).Length > 0; }
        catch { return false; }
    }
}
