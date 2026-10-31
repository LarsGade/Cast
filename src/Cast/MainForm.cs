namespace Cast;
internal sealed class MainForm : Form
{
    private readonly ListBox devices = new() { Dock = DockStyle.Fill };
    private readonly TextBox address = new() { Width = 180, PlaceholderText = "Chromecast-IP (valgfrit)" };
    private readonly Button search = new() { Text = "Find enheder", AutoSize = true };
    private readonly Button start = new() { Text = "Spejl skærmen", AutoSize = true };
    private readonly Button stop = new() { Text = "Stop", AutoSize = true, Enabled = false };
    private readonly CheckBox sound = new() { Text = "Del systemlyd", Checked = true, AutoSize = true };
    private readonly CheckBox faster = new() { Text = "Hurtigere billede (30 fps)", Checked = true, AutoSize = true };
    private readonly Button showLog = new() { Text = "Vis fejllog", AutoSize = true, Enabled = false };
    private string? lastLogPath;
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(590, 0) };
    private readonly CancellationTokenSource lifetime = new();
    private CastConnection? connection;
    private ScreenStream? screen;
    private Task? operation;
    private bool closing;
    public MainForm()
    {
        Text = "Cast — skærmspejling " + Application.ProductVersion.Split('+')[0];
        ClientSize = new Size(650, 470);
        MinimumSize = new Size(600, 440);
        Font = new Font("Segoe UI", 11);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "Din PC-skærm på Chromecast", Font = new Font(Font, FontStyle.Bold), AutoSize = true });
        layout.Controls.Add(new Label { Text = "Begge enheder skal være på samme netværk. Ingen Chrome nødvendig.", AutoSize = true, MaximumSize = new Size(580, 0), Margin = new Padding(0, 12, 0, 12) });
        layout.Controls.Add(devices);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.AddRange([search, start, stop, showLog, sound, faster]); layout.Controls.Add(buttons);
        layout.Controls.Add(address);
        layout.Controls.Add(status);
        Controls.Add(layout);
        search.Click += (_, _) => operation = RunAsync(FindAsync);
        start.Click += (_, _) => operation = RunAsync(StartAsync);
        stop.Click += (_, _) => operation = RunAsync(StopAsync);
        showLog.Click += (_, _) => ShowLog();
        Shown += (_, _) => operation = RunAsync(FindAsync);
        FormClosing += async (_, e) =>
        {
            if (closing) return;
            e.Cancel = true; closing = true; Enabled = false; lifetime.Cancel();
            if (operation is not null) await operation;
            await CleanupAsync(); Close();
        };
    }
    private async Task RunAsync(Func<Task> action)
    {
        search.Enabled = start.Enabled = stop.Enabled = sound.Enabled = faster.Enabled = false;
        try { await action(); }
        catch (Exception ex) { if (!closing) status.Text = ex is OperationCanceledException ? "Handlingen tog for lang tid." : ex.Message; }
        finally
        {
            if (!closing) { search.Enabled = start.Enabled = sound.Enabled = faster.Enabled = screen is null; stop.Enabled = screen is not null; }
        }
    }
    private async Task FindAsync()
    {
        status.Text = "Søger på lokalnetværket …";
        var found = await Discovery.FindAsync(lifetime.Token);
        devices.Items.Clear();
        foreach (var receiver in found) devices.Items.Add(receiver);
        if (devices.Items.Count > 0) devices.SelectedIndex = 0;
        status.Text = found.Count > 0 ? "Vælg en enhed og klik Spejl skærmen." : "Ingen enheder fundet. Kontrollér Wi-Fi/firewall, eller indtast enhedens IPv4-adresse fra Google Home.";
    }
    private async Task StartAsync()
    {
        string ip = address.Text.Trim();
        if (ip.Length == 0 && devices.SelectedItem is Receiver receiver) ip = receiver.Address;
        if (!System.Net.IPAddress.TryParse(ip, out var parsed) || parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new InvalidOperationException("Vælg en enhed, eller angiv dens IPv4-adresse.");
        screen = new ScreenStream();
        var session = screen;
        connection = new CastConnection();
        void Issue(string message)
        {
            if (closing || !IsHandleCreated || !ReferenceEquals(screen, session)) return;
            try { BeginInvoke((Action)(() => { if (!closing && ReferenceEquals(screen, session)) status.Text = message; })); }
            catch (InvalidOperationException) { }
        }
        screen.Issue += Issue; connection.Issue += Issue;
        try
        {
            status.Text = "Starter optagelse og forbinder til Chromecast …";
            string url = await screen.StartAsync(ip, lifetime.Token, sound.Checked, faster.Checked);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(40));
            await connection.StartAsync(ip, url, timeout.Token);
            status.Text = sound.Checked ? "Deler skærm og systemlyd. Chromecast buffer stadig billedet; markøren er ikke i realtid." : "Deler skærm uden lyd. Chromecast buffer stadig billedet; markøren er ikke i realtid.";
        }
        catch { await CleanupAsync(); throw; }
    }
    private void ShowLog()
    {
        string? path = screen?.DiagnosticLogPath ?? lastLogPath;
        if (path is null || !File.Exists(path)) return;
        try
        {
            using var dialog = new Form { Text = "Cast — fejllog", Size = new Size(850, 550), StartPosition = FormStartPosition.CenterParent };
            var text = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 10), Text = File.ReadAllText(path) };
            var copy = new Button { Text = "Kopiér log", Dock = DockStyle.Bottom, Height = 40 };
            copy.Click += (_, _) => { try { Clipboard.SetText(text.Text); } catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show("Udklipsholderen er optaget. Prøv igen."); } };
            dialog.Controls.Add(text); dialog.Controls.Add(copy); dialog.ShowDialog(this);
        }
        catch (IOException ex) { status.Text = "Loggen kunne ikke læses: " + ex.Message; }
    }

    private async Task StopAsync() { await CleanupAsync(); status.Text = "Skærmdelingen er stoppet."; }
    private async Task CleanupAsync()
    {
        // Stop capture and HTTP first, even if the receiver has gone offline.
        if (screen is not null)
        {
            lastLogPath = screen.DiagnosticLogPath;
            showLog.Enabled = lastLogPath is not null;
            await screen.DisposeAsync(); screen = null;
        }
        if (connection is not null) { await connection.DisposeAsync(); connection = null; }
    }
}
