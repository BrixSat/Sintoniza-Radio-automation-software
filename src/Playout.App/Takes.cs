using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Playout.Engine;

namespace Playout.App;

/// <summary>Where the station's takes are, and where it is (for the temperature).</summary>
/// <param name="Folder">The folder with the takes: HRS10, MIN15, TMP18, TMPM03… (see <see cref="Takes"/>).</param>
public sealed record TakesSettings(string Folder = "", double? Latitude = null, double? Longitude = null)
{
    public bool HasPlace => Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;
}

/// <summary>
/// The playout's takes: the folder, and the temperature where the station is, fetched every 10 minutes from
/// Open-Meteo (free, no account) so a take never waits for the network when it goes on air.
/// </summary>
sealed class TakeService : IDisposable
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    readonly Timer _timer;
    TakesSettings _settings = new();
    int? _temperature;
    DateTime _fetched;

    public event Action<string>? Message;

    public TakeService() => _timer = new Timer(_ => _ = Refresh(), null, TimeSpan.Zero, TimeSpan.FromMinutes(10));

    public TakesSettings Settings
    {
        get => _settings;
        set
        {
            bool place = value.Latitude != _settings.Latitude || value.Longitude != _settings.Longitude;
            _settings = value;
            if (place) { _temperature = null; _ = Refresh(); }
        }
    }

    /// <summary>The temperature, rounded (null: unknown, or older than an hour).</summary>
    public int? Temperature => DateTime.UtcNow - _fetched < TimeSpan.FromHours(1) ? _temperature : null;

    /// <summary>What the engine asks when a take starts.</summary>
    public (string Folder, int? Temperature) Source() => (_settings.Folder, Temperature);

    async Task Refresh()
    {
        var s = _settings;
        if (!s.HasPlace) return;
        try
        {
            double t = await Fetch(s.Latitude!.Value, s.Longitude!.Value);
            _temperature = (int)Math.Round(t, MidpointRounding.AwayFromZero);
            _fetched = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Message?.Invoke(L.F("Temperature not read: {0}", ex.Message));
        }
    }

    /// <summary>The temperature now at a place (°C), from Open-Meteo.</summary>
    public static async Task<double> Fetch(double latitude, double longitude)
    {
        string url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={latitude:0.####}&longitude={longitude:0.####}&current=temperature_2m");
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        return doc.RootElement.GetProperty("current").GetProperty("temperature_2m").GetDouble();
    }

    public void Dispose() => _timer.Dispose();
}
