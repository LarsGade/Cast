using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Cast;
internal sealed record Receiver(string Name, string Address)
{
    public override string ToString() => $"{Name} ({Address})";
}
internal static class Discovery
{
    public static async Task<IReadOnlyList<Receiver>> FindAsync(CancellationToken token)
    {
        var devices = new Dictionary<string, Receiver>();
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 5353));
        udp.JoinMulticastGroup(IPAddress.Parse("224.0.0.251"));
        using var query = new MemoryStream();
        query.Write(new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 });
        foreach (string label in "_googlecast._tcp.local".Split('.')) { query.WriteByte((byte)label.Length); query.Write(Encoding.ASCII.GetBytes(label)); }
        query.Write(new byte[] { 0, 0, 12, 0, 1 });
        var packet = query.ToArray();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await udp.SendAsync(packet, new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353), token);
        var names = new Dictionary<string, string>();
        var hosts = new Dictionary<string, string>();
        var addresses = new Dictionary<string, string>();
        try
        {
            while (true)
            {
                var received = await udp.ReceiveAsync(timeout.Token);
                Parse(received.Buffer, names, hosts, addresses);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        foreach (var (service, host) in hosts)
            if (addresses.TryGetValue(host, out string? ip))
                devices[ip] = new Receiver(names.GetValueOrDefault(service, service.Split('.')[0]), ip);
        return devices.Values.OrderBy(d => d.Name).ToArray();
    }

    internal static void Parse(byte[] data, Dictionary<string, string> names, Dictionary<string, string> hosts, Dictionary<string, string> addresses)
    {
        try
        {
            int offset = 0;
            int U16() { if (offset + 2 > data.Length) throw new IOException(); int n = (data[offset] << 8) | data[offset + 1]; offset += 2; return n; }
            string Name(ref int position, int depth = 0)
            {
                if (depth > 20) throw new IOException();
                var labels = new List<string>();
                while (true)
                {
                    if (position >= data.Length) throw new IOException();
                    int len = data[position++];
                    if (len == 0) break;
                    if ((len & 192) == 192)
                    {
                        if (position >= data.Length) throw new IOException();
                        int pointer = ((len & 63) << 8) | data[position++];
                        labels.Add(Name(ref pointer, depth + 1)); break;
                    }
                    if (len > 63 || position + len > data.Length) throw new IOException();
                    labels.Add(Encoding.UTF8.GetString(data, position, len)); position += len;
                }
                return string.Join('.', labels).ToLowerInvariant();
            }
            U16(); U16(); int questions = U16(); int answers = U16() + U16() + U16();
            for (int i = 0; i < questions; i++) { Name(ref offset); U16(); U16(); }
            for (int i = 0; i < answers; i++)
            {
                string owner = Name(ref offset); int type = U16(); U16(); U16(); U16(); int length = U16();
                int end = offset + length;
                if (end > data.Length) throw new IOException();
                if (type == 1 && length == 4) addresses[owner] = new IPAddress(data.AsSpan(offset, 4)).ToString();
                if (owner.EndsWith("._googlecast._tcp.local"))
                {
                    if (type == 33 && length >= 7) { int p = offset + 6; hosts[owner] = Name(ref p); }
                    if (type == 16)
                    {
                        int p = offset;
                        while (p < end)
                        {
                            int size = data[p++]; if (p + size > end) break;
                            string value = Encoding.UTF8.GetString(data, p, size); p += size;
                            if (value.StartsWith("fn=")) names[owner] = value[3..];
                        }
                    }
                }
                offset = end;
            }
        }
        catch (IOException) { /* Ignore malformed multicast traffic. */ }
    }
}
