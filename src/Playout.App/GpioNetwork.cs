using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Playout.App;

/// <summary>A text that does something when it arrives.</summary>
public sealed class NetGpioInput
{
    /// <summary>The text, as the box or desk sends it (case doesn't matter).</summary>
    public string Text { get; set; } = "";
    public GpioAction Action { get; set; }
    /// <summary>For <see cref="GpioAction.Cart"/>: which cart (1 = the first on the page).</summary>
    public int Cart { get; set; } = 1;
}

/// <summary>A text sent when a signal starts, and another when it ends.</summary>
public sealed class NetGpioOutput
{
    public GpioSignal Signal { get; set; }
    public string OnText { get; set; } = "";
    public string OffText { get; set; } = "";
}

/// <summary>
/// GPIO over the network (this computer's): texts arrive on a port — by TCP (a line each, \r\n or \n) or UDP (a
/// datagram each) — and texts go out when what is on air changes: to every TCP connection, and by UDP to an address.
/// Most GPIO-to-IP boxes and many desks can send and take such strings.
/// </summary>
public sealed class NetGpioSettings
{
    public bool Enabled { get; set; }
    /// <summary>The port it listens on, TCP and UDP.</summary>
    public int Port { get; set; } = 9100;
    /// <summary>Only from these addresses (comma-separated; empty: any). Anyone who can send to the port can press Play.</summary>
    public string AllowFrom { get; set; } = "";
    /// <summary>Where outputs also go by UDP: host:port (empty: only to the TCP connections).</summary>
    public string SendTo { get; set; } = "";
    public List<NetGpioInput> Inputs { get; set; } = new();
    public List<NetGpioOutput> Outputs { get; set; } = new();
}

/// <summary>Network GPIO at work: listens, matches texts, sends the outputs.</summary>
public sealed class NetGpio : IGpioSource
{
    readonly NetGpioSettings _s;
    readonly CancellationTokenSource _stop = new();
    readonly TcpListener _tcp;
    readonly UdpClient _udp;
    readonly List<TcpClient> _clients = new();
    readonly HashSet<IPAddress> _allowed = new();
    readonly IPEndPoint? _sendTo;
    readonly Dictionary<GpioSignal, bool> _shown = new();

    public event Action<GpioAction, int>? Fired;
    /// <summary>Every text that arrives, and from where (for Settings → Listen). Raised off the UI thread.</summary>
    public event Action<string, IPAddress>? Received;

    /// <summary>Starts listening. Throws when the port can't be used.</summary>
    public NetGpio(NetGpioSettings settings)
    {
        _s = settings;
        foreach (var part in settings.AllowFrom.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (IPAddress.TryParse(part, out var ip)) _allowed.Add(Plain(ip));
        _sendTo = Endpoint(settings.SendTo);
        _tcp = new TcpListener(IPAddress.Any, settings.Port);
        _tcp.Start();
        try { _udp = new UdpClient(new IPEndPoint(IPAddress.Any, settings.Port)); }
        catch { _tcp.Stop(); throw; }
        _ = Task.Run(AcceptLoop);
        _ = Task.Run(UdpLoop);
    }

    /// <summary>The port it really listens on (0 in the settings: one the system chose; for tests).</summary>
    public int Port => ((IPEndPoint)_tcp.LocalEndpoint).Port;

    static IPAddress Plain(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

    bool Allowed(IPAddress ip) => _allowed.Count == 0 || _allowed.Contains(Plain(ip));

    static IPEndPoint? Endpoint(string text)
    {
        text = text.Trim();
        int colon = text.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(text[(colon + 1)..], out int port) || port is < 1 or > 65535) return null;
        string host = text[..colon];
        try
        {
            var ip = IPAddress.TryParse(host, out var parsed) ? parsed : Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            return ip == null ? null : new IPEndPoint(ip, port);
        }
        catch { return null; }
    }

    async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _tcp.AcceptTcpClientAsync(_stop.Token); }
            catch { return; }
            var from = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
            if (!Allowed(from)) { client.Dispose(); continue; }
            lock (_clients) _clients.Add(client);
            _ = Task.Run(() => ReadLines(client, from));
        }
    }

    async Task ReadLines(TcpClient client, IPAddress from)
    {
        try
        {
            using var reader = new StreamReader(client.GetStream(), Encoding.UTF8);
            while (!_stop.IsCancellationRequested && await reader.ReadLineAsync(_stop.Token) is { } line)
                Take(line, from);
        }
        catch { /* the connection closed */ }
        finally
        {
            lock (_clients) _clients.Remove(client);
            client.Dispose();
        }
    }

    async Task UdpLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            UdpReceiveResult datagram;
            try { datagram = await _udp.ReceiveAsync(_stop.Token); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch { continue; } // e.g. a port unreachable from an earlier send
            if (!Allowed(datagram.RemoteEndPoint.Address)) continue;
            foreach (var line in Encoding.UTF8.GetString(datagram.Buffer).Split('\n'))
                Take(line, datagram.RemoteEndPoint.Address);
        }
    }

    void Take(string line, IPAddress from)
    {
        string text = line.Trim('\r', '\n', ' ', '\t', '\0');
        if (text.Length == 0) return;
        Received?.Invoke(text, Plain(from));
        foreach (var input in _s.Inputs)
            if (input.Action != GpioAction.None && string.Equals(input.Text.Trim(), text, StringComparison.OrdinalIgnoreCase))
                Fired?.Invoke(input.Action, input.Cart);
    }

    public void Show(GpioState state)
    {
        foreach (var output in _s.Outputs)
        {
            bool on = state.Is(output.Signal);
            if (_shown.TryGetValue(output.Signal, out bool was) && was == on) continue;
            _shown[output.Signal] = on;
            string text = on ? output.OnText : output.OffText;
            if (text.Trim().Length > 0) Send(text.Trim());
        }
    }

    /// <summary>A text to every TCP connection (a line) and to the UDP address.</summary>
    void Send(string text)
    {
        byte[] line = Encoding.UTF8.GetBytes(text + "\r\n");
        List<TcpClient> clients;
        lock (_clients) clients = _clients.ToList();
        foreach (var client in clients)
        {
            try { client.GetStream().Write(line); }
            catch { /* it went away: its reader removes it */ }
        }
        if (_sendTo != null)
        {
            try { _udp.Send(Encoding.UTF8.GetBytes(text), _sendTo); }
            catch { /* nobody there */ }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _tcp.Stop();
        _udp.Dispose();
        lock (_clients)
        {
            foreach (var client in _clients) client.Dispose();
            _clients.Clear();
        }
    }
}
