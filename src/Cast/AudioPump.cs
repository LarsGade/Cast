using System.Diagnostics;

namespace Cast;
internal static class AudioPump
{
    internal static async Task RunAsync(Stream destination, int sampleRate, int blockAlign, Action<byte[]> fill, Action discardStale, CancellationToken token)
    {
        int frames = Math.Max(1, sampleRate / 100);
        byte[] samples = new byte[frames * blockAlign];
        var clock = Stopwatch.StartNew();
        long sentFrames = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            fill(samples);
            await destination.WriteAsync(samples, token);
            sentFrames += frames;
            var delay = TimeSpan.FromSeconds((double)sentFrames / sampleRate) - clock.Elapsed;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
            else if (delay < TimeSpan.FromMilliseconds(-200)) discardStale();
        }
    }
}
