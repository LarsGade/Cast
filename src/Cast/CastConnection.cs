using System.Buffers.Binary;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Cast;

internal sealed class CastConnection : IAsyncDisposable
{
    private readonly TcpClient tcp = new();
    private SslStream? stream;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private Task? reader;
    private Task? heartbeat;
    private int? mediaSession;
    private readonly TaskCompletionSource<bool> playing = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<(string Transport, string Session)> launched = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int request;
    private string? transport;
    public event Action<string>? Issue;

    public async Task StartAsync(string address, string mediaUrl, CancellationToken token, int port = 8009)
    {
        await tcp.ConnectAsync(address, port, token);
        // Cast devices use self-signed device certificates, not public Web PKI certificates.
        stream = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
        await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = address }, token);
        reader = ReadAsync();
        heartbeat = HeartbeatAsync();
        await SendAsync("receiver-0", "connection", new { type = "CONNECT" }, token);
        await SendAsync("receiver-0", "receiver", new { type = "LAUNCH", appId = "CC1AD845", requestId = ++request }, token);
        var app = await launched.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        transport = app.Transport;
        await SendAsync(transport, "connection", new { type = "CONNECT" }, token);
        await SendAsync(transport, "media", new {
            type = "LOAD", requestId = ++request, autoplay = true,
            media = new { contentId = mediaUrl, contentType = "application/x-mpegURL", streamType = "LIVE" }
        }, token);
        await playing.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
    }

    private async Task HeartbeatAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await SendAsync("receiver-0", "heartbeat", new { type = "PING" }, lifetime.Token);
                await Task.Delay(TimeSpan.FromSeconds(5), lifetime.Token);
            }
        }
        catch (Exception ex) { if (!lifetime.IsCancellationRequested) Issue?.Invoke(ex.Message); }
    }

    private async Task SendAsync(string destination, string ns, object payload, CancellationToken token)
    {
        byte[] message = Encode(destination, "urn:x-cast:com.google.cast.tp." + ns, JsonSerializer.Serialize(payload));
        // receiver/media namespaces do not have the tp component.
        if (ns is "receiver" or "media") message = Encode(destination, "urn:x-cast:com.google.cast." + ns, JsonSerializer.Serialize(payload));
        byte[] frame = new byte[message.Length + 4];
        BinaryPrimitives.WriteInt32BigEndian(frame, message.Length);
        message.CopyTo(frame, 4);
        await writeLock.WaitAsync(token);
        try { await stream!.WriteAsync(frame, token); }
        finally { writeLock.Release(); }
    }

    internal static byte[] Encode(string destination, string ns, string json)
    {
        using var output = new MemoryStream();
        void Number(int n) { while (n > 127) { output.WriteByte((byte)((n & 127) | 128)); n >>= 7; } output.WriteByte((byte)n); }
        void Text(int field, string value) { var bytes = Encoding.UTF8.GetBytes(value); Number((field << 3) | 2); Number(bytes.Length); output.Write(bytes); }
        Number(8); Number(0); // protocol CASTV2_1_0
        Text(2, "sender-0"); Text(3, destination); Text(4, ns);
        Number(40); Number(0); Text(6, json);
        return output.ToArray();
    }

    internal static string? Payload(byte[] bytes)
    {
        int offset = 0;
        int Number() { int value = 0, shift = 0; while (true) { if (offset >= bytes.Length || shift > 28) throw new IOException("Ugyldig Cast-besked."); byte b = bytes[offset++]; value |= (b & 127) << shift; if (b < 128) return value; shift += 7; } }
        while (offset < bytes.Length)
        {
            int key = Number();
            if ((key & 7) == 0) { Number(); continue; }
            if ((key & 7) != 2) throw new IOException("Ukendt Cast-felt.");
            int length = Number();
            if (length < 0 || length > bytes.Length - offset) throw new IOException("Ugyldig Cast-længde.");
            if ((key >> 3) == 6) return Encoding.UTF8.GetString(bytes, offset, length);
            offset += length;
        }
        return null;
    }

    private async Task ReadAsync()
    {
        try
        {
            byte[] header = new byte[4];
            while (!lifetime.IsCancellationRequested)
            {
                await stream!.ReadExactlyAsync(header, lifetime.Token);
                int length = BinaryPrimitives.ReadInt32BigEndian(header);
                if (length is <= 0 or > 1048576) throw new IOException("Ugyldig Cast-pakkestørrelse.");
                byte[] data = new byte[length];
                await stream.ReadExactlyAsync(data, lifetime.Token);
                string? json = Payload(data);
                if (json is null) continue;
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var kind)) continue;
                string? type = kind.GetString();
                if (type == "PING") await SendAsync("receiver-0", "heartbeat", new { type = "PONG" }, lifetime.Token);
                if (type == "RECEIVER_STATUS" && root.GetProperty("status").TryGetProperty("applications", out var apps))
                    foreach (var app in apps.EnumerateArray())
                        if (app.GetProperty("appId").GetString() == "CC1AD845")
                            launched.TrySetResult((app.GetProperty("transportId").GetString()!, app.GetProperty("sessionId").GetString()!));
                if (type == "MEDIA_STATUS" && root.TryGetProperty("status", out var states))
                    foreach (var state in states.EnumerateArray())
                    {
                        if (state.TryGetProperty("mediaSessionId", out var session)) mediaSession = session.GetInt32();
                        if (state.TryGetProperty("playerState", out var player) && player.GetString() == "PLAYING") playing.TrySetResult(true);
                        if (state.TryGetProperty("idleReason", out var reason) && reason.GetString() == "ERROR")
                        {
                            var error = new IOException("Chromecast kunne ikke afspille strømmen. Kontrollér Windows-firewall og netværk.");
                            playing.TrySetException(error); Issue?.Invoke(error.Message);
                        }
                    }
                if (type is "LOAD_FAILED" or "LAUNCH_ERROR" or "INVALID_REQUEST")
                {
                    var error = new IOException("Chromecast afviste strømmen: " + json);
                    launched.TrySetException(error); playing.TrySetException(error); Issue?.Invoke(error.Message);
                }
            }
        }
        catch (Exception ex) { launched.TrySetException(ex); playing.TrySetException(ex); if (!lifetime.IsCancellationRequested) Issue?.Invoke(ex.Message); }
    }

    public async ValueTask DisposeAsync()
    {
        if (stream is not null && transport is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                if (mediaSession is int session)
                    await SendAsync(transport, "media", new { type = "STOP", mediaSessionId = session, requestId = Interlocked.Increment(ref request) }, timeout.Token);
                await SendAsync(transport, "connection", new { type = "CLOSE" }, timeout.Token);
            }
            catch (Exception) { }
        }
        lifetime.Cancel(); tcp.Dispose();
        if (reader is not null) await reader;
        if (heartbeat is not null) await heartbeat;
        stream?.Dispose(); lifetime.Dispose();
    }
}
