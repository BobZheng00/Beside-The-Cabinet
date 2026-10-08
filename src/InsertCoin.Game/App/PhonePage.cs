using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace InsertCoin.Game.App;

/// <summary>
/// The page a phone opens to send her photos: served by the cabinet on the home network (the PC's address there, a port
/// of its own) only while its code shows, behind a one-time key in the address. Nothing else is served, and nothing
/// goes through anyone's cloud. A photo sent there is handed to her as one dropped on her would be. In a test run it
/// listens on this PC alone, so Windows never asks about it.
/// </summary>
public sealed class PhonePage : IDisposable
{
    private const int MostHead = 16 * 1024;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly string _path, _page;

    /// <summary>Photos sent so far, waiting to be handed over on the app's own thread: their names and contents.</summary>
    public ConcurrentQueue<(string Name, byte[] Data)> Received { get; } = new();

    /// <summary>The page's address, for the code; it holds the key, so it isn't logged.</summary>
    public string Address { get; }

    /// <summary>Where it listens, without the key: "192.168.1.23:48213".</summary>
    public string Where { get; }

    private PhonePage(IPAddress address, string name)
    {
        _listener = new TcpListener(address, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)).Replace('+', '-').Replace('/', '_');
        _path = "/p/" + key;
        Where = $"{address}:{port}";
        Address = $"http://{Where}{_path}";
        _page = Page(name);
        _ = Serve(_stop.Token);
    }

    /// <summary>Opens the page on the home network, or for a test run on this PC alone; null when no home network can be found.</summary>
    /// <param name="name">Hers, for the page's heading.</param>
    public static PhonePage? Open(string name, bool thisPcOnly)
    {
        var address = thisPcOnly ? IPAddress.Loopback : HomeAddress();
        return address is null ? null : new PhonePage(address, name);
    }

    /// <summary>
    /// The PC's address on the home network: a private IPv4 address on a network that is up and has a way out (Wi-Fi
    /// or Ethernet, not a virtual switch or a tunnel).
    /// </summary>
    public static IPAddress? HomeAddress() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                        && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
            .OrderBy(n => n.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet ? 0 : 1)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses.Select(a => a.Address))
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && Private(a.GetAddressBytes()));

    private static bool Private(byte[] ip) => ip[0] == 10 || ip[0] == 172 && ip[1] is >= 16 and <= 31 || ip[0] == 192 && ip[1] == 168;

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }

    private async Task Serve(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(stop);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
            _ = Answer(client, stop);
        }
    }

    /// <summary>One request: the page, a photo sent from it, or nothing.</summary>
    private async Task Answer(TcpClient client, CancellationToken stop)
    {
        using var _ = client;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        limit.CancelAfter(TimeSpan.FromMinutes(2));         // a large photo over slow Wi-Fi, not longer
        var stream = client.GetStream();
        try
        {
            // the head: the request line and the headers, up to a blank line
            var buffer = new byte[MostHead];
            var read = 0;
            int end;
            while ((end = buffer.AsSpan(0, read).IndexOf("\r\n\r\n"u8)) < 0)
            {
                if (read == buffer.Length)
                    return;
                var got = await stream.ReadAsync(buffer.AsMemory(read), limit.Token);
                if (got == 0)
                    return;
                read += got;
            }
            var lines = Encoding.ASCII.GetString(buffer, 0, end).Split("\r\n");
            var request = lines[0].Split(' ');
            if (request.Length < 2)
                return;
            var headers = lines.Skip(1).Select(l => l.Split(':', 2)).Where(h => h.Length == 2)
                               .ToDictionary(h => h[0].Trim().ToLowerInvariant(), h => h[1].Trim());
            var (method, target) = (request[0], request[1]);

            if (method == "GET" && target == _path)
            {
                await Reply(stream, 200, "OK", "text/html; charset=utf-8", _page, limit.Token);
                return;
            }
            if (method != "POST" || target != _path + "/photo")
            {
                await Reply(stream, 404, "Not Found", "text/plain", "Not here.", limit.Token);
                return;
            }
            if (!headers.TryGetValue("content-length", out var length) || !int.TryParse(length, out var size) || size <= 0 || size > Handing.MaxBytes)
            {
                await Reply(stream, 413, "Too Large", "text/plain", "Too big: 20 MB at most.", limit.Token);
                return;
            }
            var body = new byte[size];
            var have = Math.Min(read - end - 4, size);
            Array.Copy(buffer, end + 4, body, 0, have);
            while (have < size)
            {
                var got = await stream.ReadAsync(body.AsMemory(have), limit.Token);
                if (got == 0)
                    return;
                have += got;
            }
            var name = headers.TryGetValue("x-name", out var sent) ? Path.GetFileName(Uri.UnescapeDataString(sent)) : "";
            Received.Enqueue((name.Length is > 0 and <= 100 ? name : "photo.jpg", body));
            await Reply(stream, 200, "OK", "text/plain", "Sent.", limit.Token);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or SocketException or ObjectDisposedException or UriFormatException)
        {
            // a phone that went away mid-photo, or a request that took too long: nothing comes of it
        }
    }

    private static async Task Reply(NetworkStream stream, int code, string reason, string type, string text, CancellationToken cancel)
    {
        var body = Encoding.UTF8.GetBytes(text);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\n" +
                                           "Cache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, cancel);
        await stream.WriteAsync(body, cancel);
    }

    /// <summary>The page: one big button to choose photos (or take one), and what came of sending each.</summary>
    private static string Page(string name)
    {
        var who = WebUtility.HtmlEncode(name);
        return $$"""
            <!doctype html>
            <html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Send to {{who}}</title>
            <style>
            body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:#EDE7FA;font-family:system-ui,sans-serif;color:#3B2E5E}
            main{background:#FFF7EC;border:2px solid #1E1630;border-radius:18px;padding:28px 24px;margin:16px;max-width:360px;text-align:center}
            h1{font-size:1.4em;margin:0 0 8px}p{margin:8px 0;line-height:1.4}
            label{display:inline-block;margin-top:14px;padding:14px 26px;border-radius:14px;background:#6B59A7;color:#fff;font-size:1.1em;font-weight:600}
            #said{min-height:1.4em;color:#6B59A7;font-weight:600}
            </style></head>
            <body><main>
            <h1>Send a photo to {{who}}</h1>
            <p>It lands on the slip by the cabinet, to go with what you say next.</p>
            <label>Choose photos<input id="photos" type="file" accept="image/*" multiple hidden></label>
            <p id="said"></p>
            </main>
            <script>
            const photos = document.getElementById('photos'), said = document.getElementById('said');
            photos.onchange = async () => {
              for (const photo of photos.files) {
                said.textContent = 'Sending ' + photo.name + '…';
                try {
                  const sent = await fetch(location.pathname + '/photo', { method: 'POST', body: photo,
                    headers: { 'Content-Type': photo.type || 'application/octet-stream', 'X-Name': encodeURIComponent(photo.name) } });
                  said.textContent = sent.ok ? 'Sent! Look at the cabinet.' : 'That didn\'t work (' + sent.status + ').';
                } catch (e) { said.textContent = 'The cabinet can\'t be reached. Is its code still showing?'; }
              }
              photos.value = '';
            };
            </script></body></html>
            """;
    }
}
