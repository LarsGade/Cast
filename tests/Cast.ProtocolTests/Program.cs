using System.Text;
using Cast;
object EncoderConfig() => new { output = StreamSettings.OutputArguments(true, true), audioInput = StreamSettings.AudioInputArguments("f32le", 48000, 2) };
if (args.Length == 2 && args[0] == "--screen-config-only")
{
    var options = StreamSettings.ScreenInputArguments(true);
    File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(options));
    return;
}
if (args.Length == 2 && args[0] == "--encoder-config-only")
{
    File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(EncoderConfig()));
    return;
}
int checks = 0;
void Check(bool pass, string message) { if (!pass) throw new Exception(message); checks++; }
string payload = "{\"type\":\"PING\",\"name\":\"Stue æøå\"}";
Check(CastConnection.Payload(CastConnection.Encode("receiver-0", "urn:x-cast:test", payload)) == payload, "Cast protobuf must preserve UTF-8 JSON");
string large = new('x', 500);
Check(CastConnection.Payload(CastConnection.Encode("receiver-0", "urn:x-cast:test", large)) == large, "Multi-byte protobuf lengths must roundtrip");
bool rejected = false;
try { CastConnection.Payload([50, 127, 1]); } catch (IOException) { rejected = true; }
Check(rejected, "Truncated packets must be rejected");
var names = new Dictionary<string, string>(); var hosts = new Dictionary<string, string>(); var addresses = new Dictionary<string, string>();
using var packet = new MemoryStream();
packet.Write(new byte[] { 0,0,0x84,0,0,0,0,3,0,0,0,0 });
void Name(string name) { foreach (string label in name.Split('.')) { var b = Encoding.UTF8.GetBytes(label); packet.WriteByte((byte)b.Length); packet.Write(b); } packet.WriteByte(0); }
void Record(string owner, int type, byte[] bytes) { Name(owner); packet.Write(new byte[] {0,(byte)type,0,1,0,0,0,120,(byte)(bytes.Length >> 8),(byte)bytes.Length}); packet.Write(bytes); }
Record("cast.local", 1, [192,168,1,42]);
Record("tv._googlecast._tcp.local",16, [7, (byte)'f',(byte)'n',(byte)'=',(byte)'S',(byte)'t',(byte)'u',(byte)'e']);
Record("tv._googlecast._tcp.local",33, [0,0,0,0,0x1f,0x49,4,(byte)'c',(byte)'a',(byte)'s',(byte)'t',5,(byte)'l',(byte)'o',(byte)'c',(byte)'a',(byte)'l',0]);
Discovery.Parse(packet.ToArray(), names, hosts, addresses);
Check(names["tv._googlecast._tcp.local"] == "Stue", "mDNS friendly name");
Check(hosts["tv._googlecast._tcp.local"] == "cast.local" && addresses["cast.local"] == "192.168.1.42", "mDNS service and address correlation");
Discovery.Parse([0,1], names, hosts, addresses);
Check(names.Count == 1, "Truncated discovery traffic must not destroy valid results");
Console.WriteLine($"Passed {checks} protocol checks.");

// Exercise the actual TLS/framing/read loop against a local simulated receiver.
using var rsa = System.Security.Cryptography.RSA.Create(2048);
var certRequest = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=localhost", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
using var cert = certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
listener.Start();
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var transcript = new List<string>();
var serverTask = Task.Run(async () =>
{
    using var client = await listener.AcceptTcpClientAsync(deadline.Token);
    using var tls = new System.Net.Security.SslStream(client.GetStream());
    await tls.AuthenticateAsServerAsync(new System.Net.Security.SslServerAuthenticationOptions { ServerCertificate = cert }, deadline.Token);
    async Task Reply(string json)
    {
        byte[] body = CastConnection.Encode("sender-0", "urn:x-cast:com.google.cast.receiver", json);
        byte[] header = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, body.Length);
        await tls.WriteAsync(header, deadline.Token);
        await tls.WriteAsync(body.AsMemory(0, 3), deadline.Token);
        await tls.WriteAsync(body.AsMemory(3), deadline.Token);
    }
    while (true)
    {
        byte[] header = new byte[4];
        await tls.ReadExactlyAsync(header, deadline.Token);
        byte[] body = new byte[System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header)];
        await tls.ReadExactlyAsync(body, deadline.Token);
        using var json = System.Text.Json.JsonDocument.Parse(CastConnection.Payload(body)!);
        string type = json.RootElement.GetProperty("type").GetString()!;
        transcript.Add(type);
        if (type == "LAUNCH") await Reply("""{"type":"RECEIVER_STATUS","status":{"applications":[{"appId":"CC1AD845","transportId":"transport-1","sessionId":"session-1"}]}}""");
        if (type == "LOAD")
        {
            Check(json.RootElement.GetProperty("media").GetProperty("contentId").GetString() == "http://192.168.1.2/live.m3u8", "LOAD must carry the local screen stream URL");
            await Reply("""{"type":"MEDIA_STATUS","status":[{"mediaSessionId":42,"playerState":"PLAYING"}]}""");
        }
        if (type == "STOP")
        {
            Check(json.RootElement.GetProperty("mediaSessionId").GetInt32() == 42, "STOP must address the active media session");
            return;
        }
    }
}, deadline.Token);
try
{
    await using (var cast = new CastConnection())
        await cast.StartAsync("127.0.0.1", "http://192.168.1.2/live.m3u8", deadline.Token, ((System.Net.IPEndPoint)listener.LocalEndpoint).Port);
    await serverTask;
    Check(transcript.Contains("CONNECT") && transcript.Contains("LAUNCH") && transcript.Contains("LOAD") && transcript.Contains("STOP"), "Full Cast session must launch, load, and stop");
}
finally { listener.Stop(); }
Console.WriteLine($"Passed {checks} checks including simulated Cast TLS session.");

var audioOptions = StreamSettings.OutputArguments(true, true);
Check(audioOptions.Contains("aac") && audioOptions.Contains("1:a:0") && !audioOptions.Contains("-an"), "System audio must be mapped and AAC-encoded");
Check(StreamSettings.OutputArguments(false, true).Contains("-an"), "Audio-off must remain supported");
Check(audioOptions[Array.IndexOf(audioOptions, "-r") + 1] == "30", "Faster capture must encode at 30 fps");
Check(audioOptions[Array.IndexOf(audioOptions, "-hls_list_size") + 1] == "3", "Faster profile must shorten the live window");
string playlist = "#EXTM3U\n#EXT-X-TARGETDURATION:1\n";
string prepared = StreamSettings.PreparePlaylist(playlist, true);
Check(prepared.Contains("#EXT-X-START:TIME-OFFSET=-1.5,PRECISE=YES"), "Faster playlist must request playback near the live edge");
Check(StreamSettings.PreparePlaylist(prepared, true) == prepared && StreamSettings.PreparePlaylist(playlist, false) == playlist, "Playlist hint must be idempotent and optional");
if (args.Length == 2 && args[0] == "--write-encoder-options")
    File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(EncoderConfig()));
Console.WriteLine($"Passed {checks} checks including audio mapping and live playlist settings.");

using var silence = new MemoryStream();
using var stopAudio = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
try { await AudioPump.RunAsync(silence, 48000, 8, bytes => Array.Clear(bytes), () => { }, stopAudio.Token); }
catch (OperationCanceledException) when (stopAudio.IsCancellationRequested) { }
Check(silence.Length >= 3840 && silence.ToArray().All(b => b == 0), "Idle playback device must keep supplying silence so video can start");
Check(silence.Length % (480 * 8) == 0, "Audio writes must preserve complete sample frames");
Console.WriteLine($"Passed {checks} checks including clocked silence and cancellation.");

string logDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cast-log-test-" + Guid.NewGuid().ToString("N"));
try
{
    string logPath;
    using (var log = new EncoderLog(logDirectory))
    {
        logPath = log.Path;
        log.Write("Actual input failure"); log.Write("Error opening input files: Invalid argument");
        Check(EncoderLog.Read(logPath).Contains("Actual input failure"), "Active encoder log must be readable while the encoder is still writing");
        Check(log.Details.Contains("Actual input failure"), "Root cause must survive the final generic FFmpeg error");
    }
    Check(File.ReadAllText(logPath).Contains("Actual input failure"), "Diagnostic log must survive process cleanup");
}
finally { Directory.Delete(logDirectory, true); }
Console.WriteLine($"Passed {checks} checks including retained encoder diagnostics.");

var desktopInput = StreamSettings.ScreenInputArguments(true);
Check(!desktopInput.Contains("-offset_x") && !desktopInput.Contains("-offset_y") && !desktopInput.Contains("-video_size") && desktopInput[^1] == "desktop", "Desktop capture must use gdigrab's own DPI context instead of a WinForms crop rectangle");
Console.WriteLine($"Passed {checks} checks including automatic desktop bounds.");

Check(!StreamSettings.AudioInputArguments("f32le", 48000, 2).Contains("-thread_queue_size") && !StreamSettings.ScreenInputArguments(true).Contains("-thread_queue_size"), "Modern FFmpeg's muxer-only thread_queue_size option must never be passed to inputs");
Console.WriteLine($"Passed {checks} checks including modern FFmpeg input compatibility.");
