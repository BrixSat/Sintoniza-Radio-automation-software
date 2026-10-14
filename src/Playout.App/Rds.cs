using System.Globalization;
using System.IO.Ports;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Serialization;
using Playout.Engine;

namespace Playout.App;

/// <summary>How the text gets to the RDS encoder.</summary>
public enum RdsProtocol
{
    /// <summary>Text commands such as <c>RT=…</c> and <c>PS=…</c>, one per line: PIRA32, P232, MRDS, DEVA and most other encoders.</summary>
    Ascii,
    /// <summary>The European standard binary protocol (UECP, SPB 490) of professional encoders.</summary>
    Uecp,
    /// <summary>Writes the radio text to a file, for encoders (or a website) that read one.</summary>
    TextFile,
}

public enum RdsConnection { Tcp, Udp, Serial }

/// <summary>Character set for the ASCII commands. UECP always uses the RDS (EBU) set, a text file UTF-8.</summary>
public enum RdsCharset
{
    /// <summary>Plain ASCII: accents removed ("Canção" → "Cancao"). Shows correctly on every encoder and radio.</summary>
    Ascii,
    /// <summary>The RDS character set (EBU Latin), as received by radios: keeps á, ç, õ… when the encoder passes bytes through.</summary>
    Rds,
    /// <summary>Latin-1 / Windows-1252: for encoders that convert from it themselves.</summary>
    Latin1,
    Utf8,
}

/// <summary>Now playing on FM: the radio text (RT) shows what is on air, the programme service name (PS) the station.</summary>
public sealed class RdsSettings
{
    public bool Enabled { get; set; }
    /// <summary>Programme service name, 8 characters; empty = the first 8 of the station name.</summary>
    public string Ps { get; set; } = "";
    /// <summary>Radio text while a song is on air: {artist}, {title}, {station}. Up to 64 characters are sent.</summary>
    public string Template { get; set; } = "{artist} - {title}";
    /// <summary>Radio text when no song is on air (and during jingles and adverts, with <see cref="DefaultForJingles"/>).</summary>
    public string DefaultText { get; set; } = "{station}";
    public bool DefaultForJingles { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RdsProtocol Protocol { get; set; } = RdsProtocol.Ascii;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RdsConnection Connection { get; set; } = RdsConnection.Tcp;
    public string Host { get; set; } = "";
    public int Port { get; set; } = 4001;
    public string SerialPort { get; set; } = "";
    public int BaudRate { get; set; } = 9600;
    public string FilePath { get; set; } = "";

    /// <summary>ASCII commands: {ps} and {rt} are replaced; an empty command isn't sent.</summary>
    public string PsCommand { get; set; } = "PS={ps}";
    public string RtCommand { get; set; } = "RT={rt}";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RdsCharset Charset { get; set; } = RdsCharset.Ascii;

    /// <summary>UECP addresses: 0 = every site / every encoder.</summary>
    public int SiteAddress { get; set; }
    public int EncoderAddress { get; set; }

    /// <summary>Sends everything again this often, so an encoder that restarted catches up.</summary>
    public int ResendSeconds { get; set; } = 60;

    public RdsSettings Clone() => (RdsSettings)MemberwiseClone();
}

/// <summary>Builds the RDS texts and the bytes the encoder gets.</summary>
public static class RdsText
{
    public const int PsLength = 8;
    public const int RtLength = 64;

    /// <summary>The PS (exactly 8 characters) and RT (up to 64) for what is on air (null: nothing).</summary>
    public static (string Ps, string Rt) Compose(RdsSettings s, Track? onAir, string station)
    {
        string ps = s.Ps.Trim().Length > 0 ? s.Ps.Trim() : station.Trim().ToUpperInvariant();
        ps = ps.Length > PsLength ? ps[..PsLength] : ps.PadRight(PsLength);

        bool song = onAir != null && (onAir.Kind is TrackKind.Music or TrackKind.Stream || !s.DefaultForJingles);
        string rt = song
            ? Fill(s.Template, onAir!.Artist, onAir.Title, station)
            : Fill(s.DefaultText, "", "", station);
        if (rt.Length == 0 && song) rt = Fill(s.DefaultText, "", "", station);
        return (ps, Cut(rt, RtLength));
    }

    /// <summary>The template with its fields filled in; separators left over by an empty field are dropped.</summary>
    static string Fill(string template, string artist, string title, string station)
    {
        string text = template
            .Replace("{artist}", artist.Trim(), StringComparison.OrdinalIgnoreCase)
            .Replace("{title}", title.Trim(), StringComparison.OrdinalIgnoreCase)
            .Replace("{station}", station.Trim(), StringComparison.OrdinalIgnoreCase);
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Trim(' ', '-', '–', '—', '|', '/', ':', '·');
    }

    /// <summary>Cut to <paramref name="max"/> characters, at a word if that keeps most of it.</summary>
    static string Cut(string text, int max)
    {
        if (text.Length <= max) return text;
        int space = text.LastIndexOf(' ', max);
        return (space > max * 2 / 3 ? text[..space] : text[..max]).TrimEnd();
    }

    // ───────────────────────── ASCII commands ─────────────────────────

    public static byte[] Ascii(RdsSettings s, string ps, string rt)
    {
        var text = new StringBuilder();
        if (s.PsCommand.Trim().Length > 0) text.Append(s.PsCommand.Trim().Replace("{ps}", ps)).Append("\r\n");
        if (s.RtCommand.Trim().Length > 0) text.Append(s.RtCommand.Trim().Replace("{rt}", rt)).Append("\r\n");
        return Encode(text.ToString(), s.Charset);
    }

    public static byte[] Encode(string text, RdsCharset charset) => charset switch
    {
        RdsCharset.Utf8 => Encoding.UTF8.GetBytes(text),
        RdsCharset.Latin1 => text.Select(c => c <= 0xFF ? (byte)c : (byte)PlainAscii(c)).ToArray(),
        RdsCharset.Rds => text.Select(RdsByte).ToArray(),
        _ => text.Select(c => (byte)PlainAscii(c)).ToArray(),
    };

    /// <summary>The nearest plain ASCII character: accents dropped, a few look-alikes, else '?'.</summary>
    static char PlainAscii(char c)
    {
        if (c < 0x7F) return c;
        switch (c)
        {
            case '–' or '—' or '−': return '-';
            case '‘' or '’' or '´' or '`': return '\'';
            case '“' or '”' or '«' or '»': return '"';
            case '…': return '.';
            case 'ß': return 's';
            case 'æ': return 'a';
            case 'Æ': return 'A';
            case 'ø': return 'o';
            case 'Ø': return 'O';
            case 'œ': return 'o';
            case 'Œ': return 'O';
            case 'ł': return 'l';
            case 'Ł': return 'L';
            case 'đ': return 'd';
            case 'Đ': return 'D';
        }
        string decomposed = c.ToString().Normalize(NormalizationForm.FormD);
        foreach (char d in decomposed)
            if (d < 0x7F && CharUnicodeInfo.GetUnicodeCategory(d) != UnicodeCategory.NonSpacingMark) return d;
        return '?';
    }

    // The RDS character set (EN 50067 annex E, G0 table), 0x80–0xFE; ASCII below that.
    const string RdsHigh =
        "áàéèíìóòúùÑÇŞβ¡Ĳ" + // 0x80
        "âäêëîïôöûüñçşğıĳ" + // 0x90
        "ªα©‰Ǧěňőπ€£$←↑→↓" + // 0xA0
        "º¹²³±İńűµ¿÷°¼½¾§" + // 0xB0
        "ÁÀÉÈÍÌÓÒÚÙŘČŠŽĐĿ" + // 0xC0
        "ÂÄÊËÎÏÔÖÛÜřčšžđŀ" + // 0xD0
        "ÃÅÆŒŷÝÕØÞŊŔĆŚŹŦð" + // 0xE0
        "ãåæœŵýõøþŋŕćśźŧ";   // 0xF0

    static byte RdsByte(char c)
    {
        if (c >= 0x20 && c < 0x7F && c != '$') return (byte)c; // 0x24 is ¤ in RDS; '$' is at 0xAB
        if (c is '\r' or '\n') return (byte)c;
        int i = RdsHigh.IndexOf(c);
        if (i >= 0) return (byte)(0x80 + i);
        return (byte)PlainAscii(c);
    }

    // ───────────────────────── UECP ─────────────────────────

    /// <summary>UECP frames that set the PS and the RT; <paramref name="newText"/> tells radios to clear the old text first.</summary>
    public static byte[] Uecp(RdsSettings s, string ps, string rt, bool newText)
    {
        var psMessage = new List<byte> { 0x02, 0x00, 0x00 }; // MEC PS, data set: current, programme service: 0
        psMessage.AddRange(ps.Select(RdsByte));

        var rtText = rt.Select(RdsByte).ToArray();
        var rtMessage = new List<byte> { 0x0A, 0x00, 0x00, (byte)(rtText.Length + 1) }; // MEC RT, DSN, PSN, length
        rtMessage.Add((byte)(newText ? 0x01 : 0x00)); // toggle the A/B flag (radios clear the old text); repeat until replaced
        rtMessage.AddRange(rtText);

        return [.. Frame(s, psMessage), .. Frame(s, rtMessage)];
    }

    static byte[] Frame(RdsSettings s, List<byte> message)
    {
        int address = (Math.Clamp(s.SiteAddress, 0, 1023) << 6) | Math.Clamp(s.EncoderAddress, 0, 63);
        var body = new List<byte> { (byte)(address >> 8), (byte)address, 0x00, (byte)message.Count }; // ADD, SQC (no reply), MFL
        body.AddRange(message);
        ushort crc = Crc(body);
        body.Add((byte)(crc >> 8));
        body.Add((byte)crc);

        var frame = new List<byte> { 0xFE };
        foreach (byte b in body)
        {
            // 0xFD–0xFF are reserved for framing: sent as 0xFD 0x00 / 0x01 / 0x02.
            if (b >= 0xFD) { frame.Add(0xFD); frame.Add((byte)(b - 0xFD)); }
            else frame.Add(b);
        }
        frame.Add(0xFF);
        return frame.ToArray();
    }

    /// <summary>CRC-CCITT over address to message, start 0xFFFF, inverted (UECP 2.2.8).</summary>
    public static ushort Crc(IEnumerable<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++) crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return (ushort)~crc;
    }
}

/// <summary>
/// Sends the RDS texts to the encoder: when what is on air changes, and again every
/// <see cref="RdsSettings.ResendSeconds"/>. Runs on its own thread; a connection that fails is tried again at the
/// next send, and only the latest text is ever sent (nothing piles up while the encoder is away).
/// </summary>
public sealed class RdsOutput : IDisposable
{
    readonly RdsSettings _s;
    readonly string _station;
    readonly SemaphoreSlim _wake = new(0);
    readonly CancellationTokenSource _stop = new();
    readonly object _lock = new();
    (string Ps, string Rt) _text;
    bool _newText = true;
    string? _key;
    TcpClient? _tcp;
    UdpClient? _udp;
    SerialPort? _serial;

    /// <summary>Null after a send that worked; the reason when the last one failed.</summary>
    public string? Error { get; private set; }

    /// <summary>Raised (on the RDS thread) when sending starts failing or works again: the error, or null.</summary>
    public event Action<string?>? StatusChanged;

    public RdsOutput(RdsSettings settings, string station)
    {
        _s = settings.Clone();
        _station = station;
        _text = RdsText.Compose(_s, null, station);
        Task.Run(Run);
    }

    /// <summary>What is on air now (null: nothing). Cheap to call often: sends only when the text changes.</summary>
    public void Update(Track? onAir)
    {
        string key = onAir == null ? "" : $"{onAir.Id}|{onAir.Kind}|{onAir.Artist}|{onAir.Title}";
        if (key == _key) return;
        _key = key;
        var text = RdsText.Compose(_s, onAir, _station);
        lock (_lock)
        {
            if (text == _text) return;
            _text = text;
            _newText = true;
        }
        _wake.Release();
    }

    /// <summary>Sends the current text now and reports how it went (for the Test button).</summary>
    public async Task<string?> SendNowAsync()
    {
        await Task.Run(Send);
        return Error;
    }

    async Task Run()
    {
        var token = _stop.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                Send();
                await _wake.WaitAsync(TimeSpan.FromSeconds(Math.Max(5, _s.ResendSeconds)), token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    readonly object _sendLock = new();

    void Send()
    {
        lock (_sendLock)
        {
            (string Ps, string Rt) text;
            bool newText;
            lock (_lock)
            {
                text = _text;
                newText = _newText;
                _newText = false;
            }
            string? error = null;
            try
            {
                if (_s.Protocol == RdsProtocol.TextFile) WriteFile(text.Rt);
                else Write(_s.Protocol == RdsProtocol.Uecp ? RdsText.Uecp(_s, text.Ps, text.Rt, newText) : RdsText.Ascii(_s, text.Ps, text.Rt));
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Close();
                if (newText) lock (_lock) _newText = true; // radios still need to clear the old text
            }
            if (error == Error) return;
            Error = error;
            StatusChanged?.Invoke(error);
        }
    }

    void WriteFile(string rt)
    {
        if (string.IsNullOrWhiteSpace(_s.FilePath)) throw new InvalidOperationException("No file chosen for the radio text");
        string temp = _s.FilePath + ".tmp";
        File.WriteAllText(temp, rt + Environment.NewLine, new UTF8Encoding(false));
        File.Move(temp, _s.FilePath, overwrite: true);
    }

    void Write(byte[] data)
    {
        switch (_s.Connection)
        {
            case RdsConnection.Tcp:
                if (_tcp is not { Connected: true })
                {
                    Close();
                    if (string.IsNullOrWhiteSpace(_s.Host) || _s.Port <= 0) throw new InvalidOperationException("No encoder address set");
                    var tcp = new TcpClient { NoDelay = true, SendTimeout = 5000 };
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    {
                        try { tcp.ConnectAsync(_s.Host.Trim(), _s.Port, timeout.Token).AsTask().GetAwaiter().GetResult(); }
                        catch (OperationCanceledException) { tcp.Dispose(); throw new TimeoutException($"No answer from {_s.Host}:{_s.Port}"); }
                        catch { tcp.Dispose(); throw; }
                    }
                    _tcp = tcp;
                }
                var stream = _tcp.GetStream();
                stream.Write(data);
                // Encoders answer (e.g. "+" or "OK"); nobody reads that, so empty it now and then.
                var drain = new byte[1024];
                while (_tcp.Available > 0 && stream.Read(drain) > 0) { }
                break;

            case RdsConnection.Udp:
                if (string.IsNullOrWhiteSpace(_s.Host) || _s.Port <= 0) throw new InvalidOperationException("No encoder address set");
                _udp ??= new UdpClient();
                _udp.Send(data, data.Length, _s.Host.Trim(), _s.Port);
                break;

            case RdsConnection.Serial:
                if (_serial is not { IsOpen: true })
                {
                    Close();
                    if (string.IsNullOrWhiteSpace(_s.SerialPort)) throw new InvalidOperationException("No serial port chosen");
                    var serial = new SerialPort(_s.SerialPort, _s.BaudRate, Parity.None, 8, StopBits.One) { WriteTimeout = 5000 };
                    try { serial.Open(); }
                    catch { serial.Dispose(); throw; }
                    _serial = serial;
                }
                _serial.Write(data, 0, data.Length);
                if (_serial.BytesToRead > 0) _serial.DiscardInBuffer();
                break;
        }
    }

    void Close()
    {
        try { _tcp?.Dispose(); } catch { }
        try { _udp?.Dispose(); } catch { }
        try { _serial?.Dispose(); } catch { }
        _tcp = null;
        _udp = null;
        _serial = null;
    }

    public void Dispose()
    {
        _stop.Cancel();
        lock (_sendLock) Close();
    }
}
