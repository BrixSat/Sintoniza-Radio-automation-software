using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Playout.App;

/// <summary>The web remote's settings (this computer's: the server runs on the playout).</summary>
public sealed class RemoteSettings
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 8090;
    /// <summary>Before Sintoniza users: the remote's own password (no longer used: it asks for a user).</summary>
    public string Password { get; set; } = "";
}

/// <summary>What the web remote asks the playout for, and tells it to do (answered on the UI thread).</summary>
public interface IRemoteHandler
{
    /// <summary>What is on air, what comes next, the carts, the lights.</summary>
    Task<object> Status();
    /// <summary>Does <paramref name="command"/>; the answer says whether it was done or needs confirming (an advert on air).</summary>
    Task<object> Command(JsonObject command);
    /// <summary>Songs in the library for the search box.</summary>
    Task<object> Search(string text);
    /// <summary>A Sintoniza user who may use the web remote (null: wrong name or password, or not allowed).</summary>
    Task<string?> LogIn(string user, string password);
    /// <summary>The output levels and where the item on air is, asked for several times a second (the meters).</summary>
    Task<object> Levels();
    /// <summary>The waveform of the item on air (null: none, or not read yet).</summary>
    Task<object?> Waveform();
    /// <summary>The station's logo as a PNG (null: none).</summary>
    Task<byte[]?> Logo();
}

/// <summary>
/// The web remote: a small web server on the playout (off by default) for a phone or a computer on the station's
/// network: what is on air and next, the transport, AUTO, the carts, play now / remove / add a song. A password opens
/// a session (a cookie that lasts 12 hours); five wrong passwords from an address lock it out for a minute. Plain HTTP:
/// for the station's own network, or through its VPN — not for the open internet.
/// </summary>
public sealed class RemoteServer : IDisposable
{
    const int MaxRequest = 64 * 1024;
    static readonly TimeSpan SessionLife = TimeSpan.FromHours(12);
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    readonly IRemoteHandler _handler;
    readonly TcpListener _listener;
    readonly CancellationTokenSource _stop = new();
    readonly ConcurrentDictionary<string, DateTime> _sessions = new();
    readonly ConcurrentDictionary<IPAddress, (int Failures, DateTime Until)> _failures = new();
    readonly Action<string> _log;

    public int Port { get; }

    /// <summary>Starts listening on every network address of this computer.</summary>
    public RemoteServer(RemoteSettings settings, IRemoteHandler handler, Action<string> log)
    {
        _handler = handler;
        _log = log;
        Port = settings.Port;
        _listener = new TcpListener(IPAddress.Any, Port);
        _listener.Start();
        _ = Task.Run(AcceptLoop);
    }

    /// <summary>The addresses to open on a phone: http://this computer's network addresses:port.</summary>
    public static List<string> Addresses(int port)
    {
        var list = new List<string>();
        try
        {
            foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip)) list.Add($"http://{ip}:{port}");
        }
        catch { /* no network names */ }
        if (list.Count == 0) list.Add($"http://localhost:{port}");
        return list.Distinct().ToList();
    }

    async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch { return; }
            _ = Task.Run(() => Serve(client));
        }
    }

    async Task Serve(TcpClient client)
    {
        using (client)
        {
            client.ReceiveTimeout = client.SendTimeout = 10000;
            var stream = client.GetStream();
            var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None;
            try
            {
                var request = await Read(stream);
                if (request == null) return;
                var (status, type, body, cookie) = await Route(request, remote);
                await Write(stream, status, type, body, cookie);
            }
            catch (Exception ex)
            {
                try { await Write(stream, 500, "application/json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { error = ex.Message }, Json)), null); }
                catch { /* the client went */ }
            }
        }
    }

    sealed record Request(string Method, string Path, string Query, Dictionary<string, string> Headers, byte[] Body);

    static async Task<Request?> Read(NetworkStream stream)
    {
        var buffer = new byte[8192];
        var data = new MemoryStream();
        int headerEnd = -1;
        while (headerEnd < 0)
        {
            int n = await stream.ReadAsync(buffer);
            if (n == 0) return null;
            data.Write(buffer, 0, n);
            if (data.Length > MaxRequest) return null;
            headerEnd = IndexOf(data.GetBuffer(), (int)data.Length, "\r\n\r\n"u8);
        }
        var head = Encoding.ASCII.GetString(data.GetBuffer(), 0, headerEnd).Split("\r\n");
        var first = head[0].Split(' ');
        if (first.Length < 2) return null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in head.Skip(1))
            if (line.IndexOf(':') is var c and > 0) headers[line[..c].Trim()] = line[(c + 1)..].Trim();
        int bodyStart = headerEnd + 4;
        byte[] body;
        if (headers.TryGetValue("Transfer-Encoding", out var te) && te.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            // Chunked (some clients send JSON so): size line, data, … until a 0-size chunk.
            var chunks = new MemoryStream();
            int at = bodyStart;
            while (true)
            {
                int lineEnd;
                while ((lineEnd = data.GetBuffer().AsSpan(at, (int)data.Length - at).IndexOf("\r\n"u8)) < 0)
                {
                    int n = await stream.ReadAsync(buffer);
                    if (n == 0 || data.Length > MaxRequest) return null;
                    data.Write(buffer, 0, n);
                }
                string sizeText = Encoding.ASCII.GetString(data.GetBuffer(), at, lineEnd).Split(';')[0].Trim();
                if (!int.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber, null, out int size) || size < 0 || size > MaxRequest) return null;
                at += lineEnd + 2;
                if (size == 0) break;
                while (data.Length - at < size + 2)
                {
                    int n = await stream.ReadAsync(buffer);
                    if (n == 0 || data.Length > MaxRequest) return null;
                    data.Write(buffer, 0, n);
                }
                chunks.Write(data.GetBuffer(), at, size);
                at += size + 2;
            }
            body = chunks.ToArray();
        }
        else
        {
            int length = headers.TryGetValue("Content-Length", out var l) && int.TryParse(l, out var cl) ? Math.Clamp(cl, 0, MaxRequest) : 0;
            while (data.Length - bodyStart < length)
            {
                int n = await stream.ReadAsync(buffer);
                if (n == 0) break;
                data.Write(buffer, 0, n);
            }
            body = data.GetBuffer().AsSpan(bodyStart, (int)Math.Min(length, data.Length - bodyStart)).ToArray();
        }
        string target = first[1];
        int q = target.IndexOf('?');
        return new Request(first[0].ToUpperInvariant(), q < 0 ? target : target[..q], q < 0 ? "" : target[(q + 1)..], headers, body);
    }

    static int IndexOf(byte[] data, int length, ReadOnlySpan<byte> what)
    {
        int i = data.AsSpan(0, length).IndexOf(what);
        return i;
    }

    async Task<(int Status, string Type, byte[] Body, string? Cookie)> Route(Request r, IPAddress from)
    {
        byte[] JsonBody(object o) => JsonSerializer.SerializeToUtf8Bytes(o, Json);
        if (r.Method == "GET" && (r.Path == "/" || r.Path == "/index.html"))
            return (200, "text/html; charset=utf-8", Page(), null);

        if (r.Method == "POST" && r.Path == "/api/login")
        {
            if (_failures.TryGetValue(from, out var f) && f.Failures >= 5 && DateTime.UtcNow < f.Until)
                return (429, "application/json", JsonBody(new { error = L.T("Too many wrong passwords: wait a minute.") }), null);
            var login = JsonNode.Parse(r.Body.Length > 0 ? r.Body : "{}"u8.ToArray());
            string given = login?["password"]?.GetValue<string>() ?? "", userName = login?["user"]?.GetValue<string>() ?? "";
            if (await _handler.LogIn(userName, given) is not { } who)
            {
                var now = _failures.AddOrUpdate(from, (1, DateTime.UtcNow.AddMinutes(1)), (_, old) => (old.Failures + 1, DateTime.UtcNow.AddMinutes(1)));
                if (now.Failures == 5) _log(L.F("Web remote: five wrong passwords from {0}", from));
                await Task.Delay(500); // slows guessing down
                return (401, "application/json", JsonBody(new { error = L.T("Wrong password.") }), null);
            }
            _failures.TryRemove(from, out _);
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _sessions[token] = DateTime.UtcNow + SessionLife;
            foreach (var old in _sessions.Where(s => s.Value < DateTime.UtcNow).Select(s => s.Key).ToList()) _sessions.TryRemove(old, out _);
            _log(L.F("Web remote: {0} logged in from {1}", who, from));
            return (200, "application/json", JsonBody(new { ok = true }), $"sintoniza={token}; HttpOnly; SameSite=Strict; Path=/; Max-Age={(int)SessionLife.TotalSeconds}");
        }

        if (!r.Path.StartsWith("/api/")) return (404, "text/plain", "Not found"u8.ToArray(), null);
        if (!Signed(r)) return (401, "application/json", JsonBody(new { error = L.T("Log in first.") }), null);

        switch (r.Method, r.Path)
        {
            case ("GET", "/api/status"):
                return (200, "application/json", JsonBody(await _handler.Status()), null);
            case ("GET", "/api/levels"):
                return (200, "application/json", JsonBody(await _handler.Levels()), null);
            case ("GET", "/api/waveform"):
                return (200, "application/json", JsonBody(await _handler.Waveform() ?? new { }), null);
            case ("GET", "/api/logo"):
                return await _handler.Logo() is { } png ? (200, "image/png", png, null) : (404, "text/plain", "No logo"u8.ToArray(), null);
            case ("GET", "/api/search"):
                string text = Uri.UnescapeDataString(r.Query.Split('&').FirstOrDefault(p => p.StartsWith("q="))?[2..].Replace('+', ' ') ?? "");
                return (200, "application/json", JsonBody(await _handler.Search(text)), null);
            case ("POST", "/api/command"):
                // Only from this page: a command must say it is JSON (a plain form post from another site can't).
                if (!(r.Headers.TryGetValue("Content-Type", out var type) && type.StartsWith("application/json")))
                    return (415, "application/json", JsonBody(new { error = "JSON only" }), null);
                if (JsonNode.Parse(r.Body) is not JsonObject command) return (400, "application/json", JsonBody(new { error = "No command" }), null);
                return (200, "application/json", JsonBody(await _handler.Command(command)), null);
            case ("POST", "/api/logout"):
                if (Token(r) is { } t) _sessions.TryRemove(t, out _);
                return (200, "application/json", JsonBody(new { ok = true }), "sintoniza=; Path=/; Max-Age=0");
        }
        return (404, "application/json", JsonBody(new { error = "Not found" }), null);
    }

    static string? Token(Request r) =>
        r.Headers.TryGetValue("Cookie", out var cookie)
            ? cookie.Split(';').Select(c => c.Trim()).FirstOrDefault(c => c.StartsWith("sintoniza="))?["sintoniza=".Length..]
            : null;

    bool Signed(Request r) => Token(r) is { Length: > 0 } t && _sessions.TryGetValue(t, out var until) && until > DateTime.UtcNow;

    static async Task Write(NetworkStream stream, int status, string type, byte[] body, string? cookie)
    {
        string reason = status switch { 200 => "OK", 401 => "Unauthorized", 404 => "Not Found", 415 => "Unsupported Media Type", 429 => "Too Many Requests", _ => "Error" };
        var head = new StringBuilder()
            .Append($"HTTP/1.1 {status} {reason}\r\n")
            .Append($"Content-Type: {type}\r\n")
            .Append($"Content-Length: {body.Length}\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("X-Frame-Options: DENY\r\n")
            .Append("Connection: close\r\n");
        if (cookie != null) head.Append($"Set-Cookie: {cookie}\r\n");
        head.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));
        await stream.WriteAsync(body);
    }

    static byte[]? _page;

    /// <summary>The remote's page (one file: HTML, style and script), in the program's language.</summary>
    static byte[] Page()
    {
        if (_page != null) return _page;
        using var s = typeof(RemoteServer).Assembly.GetManifestResourceStream("remote.html")!;
        using var reader = new StreamReader(s);
        string html = reader.ReadToEnd();
        // {{Text}}: the page's words, translated.
        html = System.Text.RegularExpressions.Regex.Replace(html, @"\{\{(.+?)\}\}", m => WebUtility.HtmlEncode(L.T(m.Groups[1].Value)));
        return _page = Encoding.UTF8.GetBytes(html);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _listener.Stop(); } catch { /* stopped */ }
    }
}
