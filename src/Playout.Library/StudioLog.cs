using System.Globalization;
using Dapper;

namespace Playout.Library;

/// <summary>A line of a studio's log: when (the station's time, "yyyy-MM-ddTHH:mm:ss"), whether it is a problem, and what.</summary>
public sealed record StudioLogLine(string Computer, DateTime At, bool Problem, string Line);

/// <summary>
/// The studios' logs (Sintoniza Pro → Station → Studios): each on-air computer sends the lines of its own log here, so
/// what happened on any studio can be read from the office. Kept <see cref="StudioLogKeepDays"/> days.
/// </summary>
public sealed partial class LibraryDb
{
    public const int StudioLogKeepDays = 14;
    const string LogTime = "yyyy-MM-ddTHH:mm:ss";

    /// <summary>Lines of this computer's log, in order, all in one go (none if one can't be written).</summary>
    public void AddStudioLog(long station, string computer, IReadOnlyList<(DateTime At, bool Problem, string Line)> lines)
    {
        if (lines.Count == 0) return;
        using var c = Connect();
        using var tx = c.BeginTransaction();
        c.Execute("INSERT INTO studio_log (station_id, computer, at, problem, line) VALUES (@station, @computer, @at, @problem, @line)",
            lines.Select(l => new
            {
                station, computer = Clip(computer, 100), at = l.At.ToString(LogTime, CultureInfo.InvariantCulture),
                problem = l.Problem ? 1 : 0, line = Clip(l.Line, 4000),
            }), tx);
        tx.Commit();
    }

    /// <summary>Takes out the lines older than the days kept (all stations).</summary>
    public void PruneStudioLog(DateTime now)
    {
        using var c = Connect();
        c.Execute("DELETE FROM studio_log WHERE at < @old", new { old = now.AddDays(-StudioLogKeepDays).ToString(LogTime, CultureInfo.InvariantCulture) });
    }

    /// <summary>The computers that sent lines for the station, the one heard from last first.</summary>
    public List<string> StudioLogComputers(long station)
    {
        using var c = Connect();
        return c.Query<(string Computer, string Last)>(
                "SELECT computer, MAX(at) FROM studio_log WHERE station_id = @station GROUP BY computer", new { station })
            .OrderByDescending(r => r.Last, StringComparer.Ordinal).Select(r => r.Computer).ToList();
    }

    /// <summary>A computer's lines of a day (the station's time), in order; with <paramref name="problemsOnly"/>, only the problems.</summary>
    public List<StudioLogLine> StudioLog(long station, string computer, DateTime day, bool problemsOnly = false)
    {
        using var c = Connect();
        string from = day.Date.ToString(LogTime, CultureInfo.InvariantCulture), to = day.Date.AddDays(1).ToString(LogTime, CultureInfo.InvariantCulture);
        return c.Query<(string Computer, string At, long Problem, string Line)>(
                "SELECT computer, at, problem, line FROM studio_log WHERE station_id = @station AND computer = @computer AND at >= @from AND at < @to"
                + (problemsOnly ? " AND problem = 1" : "") + " ORDER BY at, id",
                new { station, computer, from, to })
            .Select(Row).ToList();
    }

    /// <summary>The station's problems since <paramref name="since"/> (the station's time), every computer, the latest last.</summary>
    public List<StudioLogLine> StudioProblems(long station, DateTime since)
    {
        using var c = Connect();
        return c.Query<(string Computer, string At, long Problem, string Line)>(
                "SELECT computer, at, problem, line FROM studio_log WHERE station_id = @station AND problem = 1 AND at >= @since ORDER BY at, id",
                new { station, since = since.ToString(LogTime, CultureInfo.InvariantCulture) })
            .Select(Row).ToList();
    }

    static StudioLogLine Row((string Computer, string At, long Problem, string Line) r) =>
        new(r.Computer, DateTime.TryParseExact(r.At, LogTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : default, r.Problem != 0, r.Line);
}
