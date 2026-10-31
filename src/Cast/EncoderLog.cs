using System.Text;

namespace Cast;
internal sealed class EncoderLog : IDisposable
{
    private readonly object sync = new();
    private readonly Queue<string> recent = new();
    private readonly StreamWriter writer;
    private int size;
    public string Path { get; }
    public EncoderLog(string directory)
    {
        Directory.CreateDirectory(directory);
        Path = System.IO.Path.Combine(directory, $"encoder-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
        writer = new StreamWriter(Path, false, new UTF8Encoding(false)) { AutoFlush = true };
    }
    public void Write(string line)
    {
        lock (sync)
        {
            recent.Enqueue(line);
            while (recent.Count > 30) recent.Dequeue();
            if (size < 128 * 1024) { writer.WriteLine(line); size += line.Length; }
        }
    }
    public string Details { get { lock (sync) return string.Join(Environment.NewLine, recent); } }
    public void Dispose()
    {
        lock (sync)
        {
            if (size >= 128 * 1024)
            {
                writer.WriteLine("... middle of log omitted; last messages follow ...");
                foreach (string line in recent) writer.WriteLine(line);
            }
            writer.Dispose();
        }
    }
}
