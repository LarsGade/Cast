using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Cast;
internal sealed class ScreenStream : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly TcpListener server = new(IPAddress.Any, 0);
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Cast", Guid.NewGuid().ToString("N"));
    private readonly string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private Process? encoder;
    private LoopbackAudio? audio;
    private bool faster;
    private Task? serving;
    private Task? logging;
    private string lastError = "";
    public event Action<string>? Issue;

    public async Task<string> StartAsync(string receiverAddress, CancellationToken token, bool includeAudio = true, bool faster = true)
    {
        this.faster = faster;
        Directory.CreateDirectory(directory);
        string binary = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (!File.Exists(binary))
        {
            binary = Path.Combine(directory, "ffmpeg.exe");
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Cast.ffmpeg.exe")
                ?? throw new FileNotFoundException("Videokoderen mangler. Byg appen med build.ps1.");
            await using var target = File.Create(binary);
            await resource.CopyToAsync(target, token);
        }
        using var route = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        route.Connect(receiverAddress, 8009);
        string localAddress = ((IPEndPoint)route.LocalEndPoint!).Address.ToString();
        server.Start();
        serving = ServeAsync();
        var bounds = SystemInformation.VirtualScreen;
        if (includeAudio)
        {
            try { audio = new LoopbackAudio(); }
            catch (Exception ex) { throw new IOException("Systemlyden kunne ikke åbnes. Kontrollér Windows' standardhøjttaler, eller slå Del systemlyd fra. " + ex.Message, ex); }
            audio.Issue += message => Issue?.Invoke(message);
        }
        var start = new ProcessStartInfo(binary) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardInput = includeAudio, WorkingDirectory = directory };
        var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-thread_queue_size", "32", "-probesize", "32", "-analyzeduration", "0", "-f", "gdigrab", "-framerate", faster ? "30" : "20", "-draw_mouse", "1",
            "-offset_x", bounds.Left.ToString(), "-offset_y", bounds.Top.ToString(), "-video_size", $"{bounds.Width}x{bounds.Height}", "-i", "desktop" };
        if (audio is not null)
            args.AddRange(["-thread_queue_size", "64", "-probesize", "32", "-analyzeduration", "0", "-f", audio.Format, "-ar", audio.SampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture), "-ac", audio.Channels.ToString(System.Globalization.CultureInfo.InvariantCulture), "-i", "pipe:0"]);
        args.AddRange(StreamSettings.OutputArguments(includeAudio, faster));
        foreach (string argument in args) start.ArgumentList.Add(argument);
        encoder = Process.Start(start) ?? throw new IOException("Skærmoptagelsen kunne ikke startes.");
        audio?.Start(encoder.StandardInput.BaseStream);
        logging = Task.Run(async () => { while (await encoder.StandardError.ReadLineAsync() is { } line) lastError = line; if (!lifetime.IsCancellationRequested) Issue?.Invoke("Skærmoptagelsen stoppede: " + lastError); });
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
        wait.CancelAfter(TimeSpan.FromSeconds(20));
        while (!File.Exists(Path.Combine(directory, faster ? "part000001.ts" : "part000002.ts")))
        {
            if (encoder.HasExited) throw new IOException("Videokoderen stoppede: " + lastError);
            await Task.Delay(200, wait.Token);
        }
        return $"http://{localAddress}:{((IPEndPoint)server.LocalEndpoint).Port}/{secret}/live.m3u8";
    }

    private async Task ServeAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var client = await server.AcceptTcpClientAsync(lifetime.Token);
                _ = HandleAsync(client);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) when (lifetime.IsCancellationRequested) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                string? request = await reader.ReadLineAsync(timeout.Token);
                var parts = request?.Split(' ');
                if (parts is null || parts.Length < 2) return;
                int headerSize = request!.Length;
                while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 } line)
                    if ((headerSize += line.Length) > 16384) return;
                string prefix = "/" + secret + "/";
                string filename = parts[1].StartsWith(prefix) ? parts[1][prefix.Length..] : "";
                bool valid = filename == "live.m3u8" || (filename.StartsWith("part") && filename.EndsWith(".ts") && filename.Length == 13 && filename[4..10].All(char.IsAsciiDigit));
                string path = Path.Combine(directory, valid ? filename : "missing");
                bool found = valid && File.Exists(path) && parts[0] is "GET" or "HEAD";
                byte[] body = found ? await File.ReadAllBytesAsync(path, timeout.Token) : [];
                if (found && filename == "live.m3u8")
                    body = Encoding.UTF8.GetBytes(StreamSettings.PreparePlaylist(Encoding.UTF8.GetString(body), faster));
                string type = filename.EndsWith(".m3u8") ? "application/vnd.apple.mpegurl" : "video/mp2t";
                string header = $"HTTP/1.1 {(found ? "200 OK" : "404 Not Found")}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nAccess-Control-Allow-Origin: *\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header), timeout.Token);
                if (parts[0] != "HEAD") await stream.WriteAsync(body, timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); server.Stop();
        audio?.Cancel();
        if (encoder is not null)
        {
            if (!encoder.HasExited) { encoder.Kill(true); await encoder.WaitForExitAsync(); }
            if (logging is not null) await logging;
            encoder.Dispose();
        }
        if (audio is not null) { await audio.DisposeAsync(); audio = null; }
        if (serving is not null) await serving;
        try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
