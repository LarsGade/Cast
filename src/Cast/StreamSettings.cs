using System.Globalization;

namespace Cast;
internal static class StreamSettings
{
    public static string[] OutputArguments(bool includeAudio, bool faster)
    {
        int fps = faster ? 30 : 20;
        var args = new List<string> { "-map", "0:v:0" };
        if (includeAudio)
            args.AddRange(["-map", "1:a:0", "-c:a", "aac", "-b:a", "160k", "-ar", "48000", "-ac", "2", "-af", "aresample=async=1:first_pts=0"]);
        else args.Add("-an");
        args.AddRange(["-vf", "scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2,format=yuv420p", "-r", fps.ToString(CultureInfo.InvariantCulture),
            "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency", "-bf", "0", "-profile:v", "main", "-level", "3.1",
            "-b:v", "3000k", "-maxrate", "4000k", "-bufsize", faster ? "2000k" : "6000k", "-g", fps.ToString(CultureInfo.InvariantCulture), "-sc_threshold", "0",
            "-f", "hls", "-hls_time", "1", "-hls_list_size", faster ? "3" : "6", "-hls_flags", "delete_segments+independent_segments+temp_file", "-hls_segment_filename", "part%06d.ts", "live.m3u8"]);
        return args.ToArray();
    }

    public static string PreparePlaylist(string text, bool faster)
    {
        // A hint, not a guarantee: the receiver still controls its playback buffer.
        if (!faster || text.Contains("#EXT-X-START:")) return text;
        return text.Replace("#EXTM3U", "#EXTM3U\n#EXT-X-START:TIME-OFFSET=-1.5,PRECISE=YES");
    }
}
