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
    private Task? serving;
    private Task? logging;
    private string lastError = "";
    public event Action<string>? Issue;

    public async Task<string> StartAsync(string receiverAddress, CancellationToken token)
    {
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
        var start = new ProcessStartInfo(binary) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, WorkingDirectory = directory };
        string[] args = ["-hide_banner", "-loglevel", "warning", "-f", "gdigrab", "-framerate", "20", "-draw_mouse", "1",
            "-offset_x", bounds.Left.ToString(), "-offset_y", bounds.Top.ToString(), "-video_size", $"{bounds.Width}x{bounds.Height}", "-i", "desktop",
            "-an", "-vf", "scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2,format=yuv420p", "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency", "-profile:v", "main", "-level", "3.1",
            "-b:v", "3000k", "-maxrate", "4000k", "-bufsize", "6000k", "-g", "20", "-sc_threshold", "0", "-f", "hls", "-hls_time", "1", "-hls_list_size", "6", "-hls_flags", "delete_segments+independent_segments", "-hls_segment_filename", "part%06d.ts", "live.m3u8"];
        foreach (string argument in args) start.ArgumentList.Add(argument);
        encoder = Process.Start(start) ?? throw new IOException("Skærmoptagelsen kunne ikke startes.");
        logging = Task.Run(async () => { while (await encoder.StandardError.ReadLineAsync() is { } line) lastError = line; if (!lifetime.IsCancellationRequested) Issue?.Invoke("Skærmoptagelsen stoppede: " + lastError); });
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
        wait.CancelAfter(TimeSpan.FromSeconds(20));
        while (!File.Exists(Path.Combine(directory, "part000002.ts")))
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
        if (encoder is not null)
        {
            if (!encoder.HasExited) { encoder.Kill(true); await encoder.WaitForExitAsync(); }
            if (logging is not null) await logging;
            encoder.Dispose();
        }
        if (serving is not null) await serving;
        try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
