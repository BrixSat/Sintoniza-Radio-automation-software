using System.Data.Common;
using System.Globalization;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Playout.Library;

public sealed partial class LibraryDb
{
    /// <summary>
    /// A copy of the whole database, made while it is in use: SQLite with its own backup (a .db file), MySQL as an SQL
    /// dump made here (no mysqldump needed: a .sql file that puts every table back with <c>mysql name &lt; file</c>).
    /// Returns the file written: <paramref name="pathWithoutExtension"/> plus .db or .sql.
    /// </summary>
    public string Backup(string pathWithoutExtension)
    {
        string file = pathWithoutExtension + (_mySql ? ".sql" : ".db");
        if (_mySql) DumpMySql(file);
        else
        {
            using var source = (SqliteConnection)Connect();
            using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString());
            target.Open();
            source.BackupDatabase(target);
        }
        return file;
    }

    void DumpMySql(string file)
    {
        using var c = Connect();
        // Every table as it was at one moment, while the studios keep writing (a schedule saved during the backup
        // is in it whole or not at all).
        c.Execute("SET SESSION TRANSACTION ISOLATION LEVEL REPEATABLE READ");
        c.Execute("START TRANSACTION WITH CONSISTENT SNAPSHOT, READ ONLY");
        string tmp = file + ".part";
        using (var w = new StreamWriter(tmp, false, new UTF8Encoding(false)))
        {
            w.WriteLine($"-- Sintoniza database backup, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            w.WriteLine("SET NAMES utf8mb4;");
            w.WriteLine("SET FOREIGN_KEY_CHECKS = 0;");
            foreach (string table in c.Query<string>("SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE() AND table_type = 'BASE TABLE' ORDER BY table_name"))
            {
                string create = c.QueryFirst<(string Name, string Sql)>($"SHOW CREATE TABLE `{table}`").Sql;
                w.WriteLine();
                w.WriteLine($"DROP TABLE IF EXISTS `{table}`;");
                w.WriteLine(create + ";");
                using var cmd = c.CreateCommand();
                cmd.CommandText = $"SELECT * FROM `{table}`";
                using var r = cmd.ExecuteReader();
                var row = new string[r.FieldCount];
                var batch = new StringBuilder();
                int rows = 0;
                while (r.Read())
                {
                    for (int i = 0; i < r.FieldCount; i++) row[i] = Literal(r, i);
                    batch.Append(rows == 0 ? $"INSERT INTO `{table}` VALUES\n(" : ",\n(").Append(string.Join(",", row)).Append(')');
                    if (++rows == 500) { w.WriteLine(batch + ";"); batch.Clear(); rows = 0; }
                }
                if (rows > 0) w.WriteLine(batch + ";");
            }
            w.WriteLine();
            w.WriteLine("SET FOREIGN_KEY_CHECKS = 1;");
        }
        c.Execute("COMMIT");
        File.Move(tmp, file, true);
    }

    /// <summary>A value as MySQL reads it back exactly.</summary>
    static string Literal(DbDataReader r, int i)
    {
        if (r.IsDBNull(i)) return "NULL";
        return r.GetValue(i) switch
        {
            string s => Quote(s),
            byte[] b => b.Length == 0 ? "''" : "0x" + Convert.ToHexString(b),
            bool b => b ? "1" : "0",
            DateTime d => Quote(d.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture)),
            DateTimeOffset d => Quote(d.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture)),
            TimeSpan t => Quote(t.ToString("c", CultureInfo.InvariantCulture)),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            var v => Quote(v.ToString() ?? ""),
        };
    }

    static string Quote(string s) => "'" + s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\0", "\\0").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\x1a", "\\Z") + "'";
}
