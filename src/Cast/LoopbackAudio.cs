using NAudio.Wave;

namespace Cast;

// Captures the default Windows playback device, not the microphone.
internal sealed class LoopbackAudio : IAsyncDisposable
{
    private readonly WasapiLoopbackCapture capture;
    private readonly BufferedWaveProvider buffer;
    private readonly CancellationTokenSource lifetime = new();
    private Task? pump;
    public event Action<string>? Issue;
    public string Format { get; }
    public int SampleRate => capture.WaveFormat.SampleRate;
    public int Channels => capture.WaveFormat.Channels;

    public LoopbackAudio()
    {
        try
        {
            capture = new WasapiLoopbackCapture();
            var wave = capture.WaveFormat;
            var encoding = wave.Encoding;
            if (wave is WaveFormatExtensible extended)
                encoding = extended.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71") ? WaveFormatEncoding.IeeeFloat
                    : extended.SubFormat == new Guid("00000001-0000-0010-8000-00aa00389b71") ? WaveFormatEncoding.Pcm : encoding;
            Format = (encoding, wave.BitsPerSample) switch
            {
                (WaveFormatEncoding.IeeeFloat, 32) => "f32le",
                (WaveFormatEncoding.Pcm, 16) => "s16le",
                (WaveFormatEncoding.Pcm, 24) => "s24le",
                (WaveFormatEncoding.Pcm, 32) => "s32le",
                _ => throw new NotSupportedException("Lydformatet understøttes ikke. Vælg stereo 16/24-bit eller 32-bit float i Windows lydindstillinger.")
            };
            buffer = new BufferedWaveProvider(wave) { BufferDuration = TimeSpan.FromMilliseconds(200), DiscardOnBufferOverflow = true, ReadFully = true };
            capture.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            capture.RecordingStopped += (_, e) =>
            {
                if (!lifetime.IsCancellationRequested && e.Exception is not null)
                    Issue?.Invoke("Systemlyden stoppede. Stop og start spejlingen igen: " + e.Exception.Message);
            };
        }
        catch
        {
            capture?.Dispose();
            throw;
        }
    }

    public void Start(Stream destination)
    {
        capture.StartRecording();
        pump = PumpAsync(destination);
    }

    private async Task PumpAsync(Stream destination)
    {
        // WASAPI does not emit samples while the device is idle. Keep sending
        // silence on a clock so FFmpeg can start and video never waits for sound.
        try
        {
            await AudioPump.RunAsync(destination, SampleRate, capture.WaveFormat.BlockAlign,
                samples => buffer.Read(samples, 0, samples.Length), buffer.ClearBuffer, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            if (!lifetime.IsCancellationRequested) Issue?.Invoke("Lydforbindelsen til videokoderen blev afbrudt: " + ex.Message);
        }
    }

    public void Cancel() => lifetime.Cancel();

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        capture.StopRecording();
        if (pump is not null) await pump;
        capture.Dispose();
        lifetime.Dispose();
    }
}
