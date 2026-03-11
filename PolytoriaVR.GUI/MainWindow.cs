using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PolytoriaVR.GUI;

public class MainWindow : Form
{
    private const int Port = 9999;
    private static readonly bool IsWine = DetectWine(); // the mod won't run on Linux, however installer will still work if under wine
    private static readonly string GameDir = ResolveGameDir();
    private static readonly string BootConfig = Path.Combine(GameDir, "Polytoria Client_Data", "boot.config");
    private static readonly string BootBackup = BootConfig + ".bak";
    private static readonly string SettingsFile = Path.Combine(
        AppContext.BaseDirectory, "polytoriavr_settings.json");

    private static bool DetectWine()
    {

        string[] wineVars = { "WINEPREFIX", "WINELOADERNOEXEC", "WINELOADER", "WINEDEBUG", "WINE_LARGE_ADDRESS_AWARE" };
        foreach (var v in wineVars)
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(v))) return true;

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Wine");
            if (key != null) return true;
        }
        catch { }

        try
        {
            var ntdll = GetModuleHandle("ntdll.dll");
            if (ntdll != IntPtr.Zero && GetProcAddressWine(ntdll, "wine_get_version") != IntPtr.Zero)
                return true;
        }
        catch { }
        return false;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "GetProcAddress", CharSet = System.Runtime.InteropServices.CharSet.Ansi)]
    private static extern IntPtr GetProcAddressWine(IntPtr hModule, string lpProcName);

    private static string? FindClientDir(string clientBase)
    {
        try
        {
            if (!Directory.Exists(clientBase)) return null;
            var dirs = Directory.GetDirectories(clientBase);
            return dirs.Length > 0 ? dirs[0] : null;
        }
        catch { return null; }
    }

    private static string ResolveGameDir()
    {
        if (IsWine)
        {

            var homes = new System.Collections.Generic.List<string>();

            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
                homes.Add("Z:" + home.Replace('/', '\\'));

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var userName = Path.GetFileName(userProfile.TrimEnd('\\', '/'));
            if (!string.IsNullOrEmpty(userName))
                homes.Add(@"Z:\home\" + userName);

            var user = Environment.GetEnvironmentVariable("USER");
            if (!string.IsNullOrEmpty(user))
                homes.Add(@"Z:\home\" + user);

            string[] configPaths = {
                Path.Combine(".var", "app", "com.polytoria.launcher", "config", "Polytoria", "Client"),
                Path.Combine(".config", "Polytoria", "Client"),
                Path.Combine(".local", "share", "Polytoria", "Client"),
            };

            foreach (var h in homes)
            {
                foreach (var cfg in configPaths)
                {
                    var candidate = Path.Combine(h, cfg);
                    var found = FindClientDir(candidate);
                    if (found != null) return found;
                }
            }
        }

        var winBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Polytoria", "Client");
        return FindClientDir(winBase) ?? winBase;
    }

    private const string BepInExUrlWin = "https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755%2B3fab71a.zip";
    private const string BepInExUrlLinux = "https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-linux-x64-6.0.0-be.755%2B3fab71a.zip";
    private static readonly string BepInExUrl = IsWine ? BepInExUrlLinux : BepInExUrlWin;

    private readonly Label statusLabel;
    private readonly NumericUpDown turnSpeedInput;
    private readonly NumericUpDown vrScaleInput;
    private readonly CheckBox localHandsCheck;
    private readonly CheckBox flyCheck;
    private readonly NumericUpDown flySpeedInput;
    private readonly System.Windows.Forms.Timer flySpeedDebounce;
    private readonly TextBox logBox;
    private readonly Button installBtn;
    private readonly ProgressBar progressBar;

    private readonly System.Windows.Forms.Timer turnSpeedDebounce;
    private readonly System.Windows.Forms.Timer scaleDebounce;
    private bool syncing;
    private static readonly HttpClient http = new();

    public MainWindow()
    {
        Text = "PolytoriaVR";
        ClientSize = new Size(380, 400);
        MinimumSize = new Size(340, 350);
        StartPosition = FormStartPosition.CenterScreen;

        turnSpeedDebounce = new System.Windows.Forms.Timer { Interval = 300 };
        turnSpeedDebounce.Tick += TurnSpeedDebounce_Tick;
        scaleDebounce = new System.Windows.Forms.Timer { Interval = 300 };
        scaleDebounce.Tick += ScaleDebounce_Tick;
        flySpeedDebounce = new System.Windows.Forms.Timer { Interval = 300 };
        flySpeedDebounce.Tick += FlySpeedDebounce_Tick;

        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 268,
            Padding = new Padding(10, 8, 10, 0)
        };
        Controls.Add(topPanel);

        int y = 8;

        statusLabel = new Label { Text = "Idle", Location = new Point(10, y), AutoSize = true };
        topPanel.Controls.Add(statusLabel);
        y += 24;

        var activateBtn = new Button { Text = "Activate VR", Location = new Point(10, y), Size = new Size(170, 28) };
        activateBtn.Click += ActivateVR_Click;
        topPanel.Controls.Add(activateBtn);

        var deactivateBtn = new Button { Text = "Deactivate VR", Location = new Point(185, y), Size = new Size(170, 28) };
        deactivateBtn.Click += DeactivateVR_Click;
        topPanel.Controls.Add(deactivateBtn);
        y += 38;

        var settingsGroup = new GroupBox { Text = "Settings", Location = new Point(10, y), Size = new Size(345, 143), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        topPanel.Controls.Add(settingsGroup);

        int sy = 20;
        settingsGroup.Controls.Add(new Label { Text = "Turn Speed:", Location = new Point(10, sy + 2), AutoSize = true });
        turnSpeedInput = new NumericUpDown { Minimum = 30, Maximum = 360, Value = 120, Increment = 10, Location = new Point(100, sy), Width = 65 };
        turnSpeedInput.ValueChanged += (_, _) => { if (!syncing) { turnSpeedDebounce.Stop(); turnSpeedDebounce.Start(); } };
        settingsGroup.Controls.Add(turnSpeedInput);
        settingsGroup.Controls.Add(new Label { Text = "deg/s", Location = new Point(170, sy + 2), AutoSize = true });
        sy += 28;

        settingsGroup.Controls.Add(new Label { Text = "VR Scale:", Location = new Point(10, sy + 2), AutoSize = true });
        vrScaleInput = new NumericUpDown { Minimum = 0.5m, Maximum = 20.0m, Value = 1.0m, Increment = 0.1m, DecimalPlaces = 1, Location = new Point(100, sy), Width = 65 };
        vrScaleInput.ValueChanged += (_, _) => { if (!syncing) { scaleDebounce.Stop(); scaleDebounce.Start(); } };
        settingsGroup.Controls.Add(vrScaleInput);
        sy += 28;

        localHandsCheck = new CheckBox { Text = "Show Local only Hands", Checked = true, Location = new Point(10, sy), AutoSize = true };
        localHandsCheck.CheckedChanged += LocalHands_Changed;
        settingsGroup.Controls.Add(localHandsCheck);

        flyCheck = new CheckBox { Text = "Fly Mode", Checked = true, Location = new Point(170, sy), AutoSize = true };
        flyCheck.CheckedChanged += Fly_Changed;
        settingsGroup.Controls.Add(flyCheck);
        sy += 28;

        settingsGroup.Controls.Add(new Label { Text = "Fly Speed:", Location = new Point(10, sy + 2), AutoSize = true });
        flySpeedInput = new NumericUpDown { Minimum = 0.5m, Maximum = 10.0m, Value = 1.0m, Increment = 0.5m, DecimalPlaces = 1, Location = new Point(100, sy), Width = 65 };
        flySpeedInput.ValueChanged += (_, _) => { if (!syncing) { flySpeedDebounce.Stop(); flySpeedDebounce.Start(); } };
        settingsGroup.Controls.Add(flySpeedInput);

        y += 151;

        installBtn = new Button { Text = "Install Mod", Location = new Point(10, y), Size = new Size(345, 28), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        installBtn.Click += InstallMod_Click;
        topPanel.Controls.Add(installBtn);
        y += 32;

        progressBar = new ProgressBar { Location = new Point(10, y), Size = new Size(345, 18), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Visible = false };
        topPanel.Controls.Add(progressBar);

        logBox = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            WordWrap = true, Dock = DockStyle.Fill,
            Font = new Font("Consolas", 8.5f)
        };
        Controls.Add(logBox);
        logBox.BringToFront();

        Log("PolytoriaVR Remote ready.");
        Log($"Game: {GameDir}");

        LoadSettings();
    }

    private async Task<string> SendCommandAsync(string cmd)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", Port);
            var stream = client.GetStream();
            stream.ReadTimeout = 3000;
            await stream.WriteAsync(Encoding.UTF8.GetBytes(cmd));
            var buf = new byte[1024];
            int n = await stream.ReadAsync(buf, 0, buf.Length);
            return Encoding.UTF8.GetString(buf, 0, n);
        }
        catch (SocketException) { return "ERROR: Game not running or mod not loaded"; }
        catch (IOException) { return "ERROR: Timeout"; }
        catch (Exception e) { return $"ERROR: {e.Message}"; }
    }

    private async void ActivateVR_Click(object? sender, EventArgs e)
    {
        await SendCommandAsync($"SET_TURN_SPEED:{turnSpeedInput.Value.ToString(CultureInfo.InvariantCulture)}");
        await SendCommandAsync($"SET_SCALE:{vrScaleInput.Value.ToString(CultureInfo.InvariantCulture)}");
        await SendCommandAsync($"SET_LOCAL_HANDS:{(localHandsCheck.Checked ? "1" : "0")}");
        await SendCommandAsync($"SET_FLY:{(flyCheck.Checked ? "1" : "0")}");
        await SendCommandAsync($"SET_FLY_SPEED:{flySpeedInput.Value.ToString(CultureInfo.InvariantCulture)}");

        var resp = await SendCommandAsync("START_VR");
        Log($"Activate: {resp}");
        statusLabel.Text = resp;
    }

    private async void DeactivateVR_Click(object? sender, EventArgs e)
    {
        var resp = await SendCommandAsync("STOP_VR");
        Log($"Deactivate: {resp}");
        statusLabel.Text = resp;
    }

    private async void InstallMod_Click(object? sender, EventArgs e)
    {
        installBtn.Enabled = false;
        installBtn.Text = "Installing...";
        progressBar.Visible = true;
        progressBar.Value = 0;

        try
        {
            Log(IsWine ? $"Installing to Linux path: {GameDir}" : $"Installing to: {GameDir}");
            if (!Directory.Exists(GameDir))
            {
                Log($"Game directory not found: {GameDir}");
                return;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "PolytoriaVR_Install");
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);

            Log("Downloading BepInEx...");
            progressBar.Value = 5;
            var bepZip = Path.Combine(tempDir, "bepinex.zip");
            await DownloadFileAsync(BepInExUrl, bepZip);
            progressBar.Value = 25;

            Log("Extracting BepInEx...");
            ZipFile.ExtractToDirectory(bepZip, GameDir, true);
            Log("BepInEx installed.");
            progressBar.Value = 40;

            // we don't need patches since fixes were merged

            var pluginsDir = Path.Combine(GameDir, "BepInEx", "plugins");
            Directory.CreateDirectory(pluginsDir);

            Log("Installing mod...");
            string? modDir = null;
            var exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (File.Exists(Path.Combine(exeDir, "PolytoriaVR.dll")))
                modDir = exeDir;
            else
            {
                var search = Directory.GetParent(exeDir);
                for (int depth = 0; depth < 5 && search != null; depth++, search = search.Parent)
                {
                    var candidate = Path.Combine(search.FullName, "build");
                    if (File.Exists(Path.Combine(candidate, "PolytoriaVR.dll")))
                    { modDir = candidate; break; }
                }
            }
            if (modDir == null)
            {
                Log("ERROR: Mod files not found. Build the mod project first (dotnet build).");
                return;
            }
            foreach (var file in Directory.GetFiles(modDir))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("PolytoriaVR.GUI", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                if (name.Equals("openvr_api.dll", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("openvr_api.so", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("openvr_api.dylib", StringComparison.OrdinalIgnoreCase))
                {
                    var dest = Path.Combine(GameDir, name);
                    File.Copy(file, dest, true);
                }
                else
                {
                    var dest = Path.Combine(pluginsDir, name);
                    File.Copy(file, dest, true);
                }
                Log($"  Copied: {name}");
            }
            Log("Mod files installed.");
            progressBar.Value = 90;

            PatchBootConfig();
            progressBar.Value = 100;

            try { Directory.Delete(tempDir, true); } catch { }

            Log("Installation complete! Restart the game.");
            statusLabel.Text = "Installed";
        }
        catch (Exception ex)
        {
            Log($"Install error: {ex.Message}");
            statusLabel.Text = "Install failed";
        }
        finally
        {
            installBtn.Enabled = true;
            installBtn.Text = "Install Mod";
            progressBar.Visible = false;
        }
    }

    private async Task DownloadFileAsync(string url, string destPath)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        using var fs = new FileStream(destPath, FileMode.Create);
        await response.Content.CopyToAsync(fs);
    }

    private static string? FindFolder(string root, string folderName)
    {
        if (Path.GetFileName(root).Equals(folderName, StringComparison.OrdinalIgnoreCase))
            return root;
        foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(dir).Equals(folderName, StringComparison.OrdinalIgnoreCase))
                return dir;
        }
        return null;
    }

    private async void TurnSpeedDebounce_Tick(object? sender, EventArgs e)
    {
        turnSpeedDebounce.Stop();
        SaveSettings();
        var resp = await SendCommandAsync($"SET_TURN_SPEED:{turnSpeedInput.Value.ToString(CultureInfo.InvariantCulture)}");
        if (resp.Contains("ERROR") && !resp.Contains("Game not running"))
            Log($"Set turn speed: {resp}");
    }

    private async void ScaleDebounce_Tick(object? sender, EventArgs e)
    {
        scaleDebounce.Stop();
        SaveSettings();
        var resp = await SendCommandAsync($"SET_SCALE:{vrScaleInput.Value.ToString(CultureInfo.InvariantCulture)}");
        if (resp.Contains("ERROR") && !resp.Contains("Game not running"))
            Log($"Set VR scale: {resp}");
    }

    private async void FlySpeedDebounce_Tick(object? sender, EventArgs e)
    {
        flySpeedDebounce.Stop();
        SaveSettings();
        var resp = await SendCommandAsync($"SET_FLY_SPEED:{flySpeedInput.Value.ToString(CultureInfo.InvariantCulture)}");
        if (resp.Contains("ERROR") && !resp.Contains("Game not running"))
            Log($"Set fly speed: {resp}");
    }

    private async void LocalHands_Changed(object? sender, EventArgs e)
    {
        if (syncing) return;
        SaveSettings();
        var resp = await SendCommandAsync($"SET_LOCAL_HANDS:{(localHandsCheck.Checked ? "1" : "0")}");
        Log($"Local hands {(localHandsCheck.Checked ? "shown" : "hidden")}: {resp}");
    }

    private async void Fly_Changed(object? sender, EventArgs e)
    {
        if (syncing) return;
        SaveSettings();
        var resp = await SendCommandAsync($"SET_FLY:{(flyCheck.Checked ? "1" : "0")}");
        Log($"Fly mode {(flyCheck.Checked ? "enabled" : "disabled")}: {resp}");
    }

    private void PatchBootConfig()
    {
        if (!File.Exists(BootConfig)) { Log($"boot.config not found: {BootConfig}"); return; }
        if (!File.Exists(BootBackup)) { File.Copy(BootConfig, BootBackup, false); Log("Backed up boot.config"); }

        var lines = File.ReadAllLines(BootConfig).ToList();
        bool found = false;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("vr-enabled="))
            {
                if (lines[i] != "vr-enabled=1") lines[i] = "vr-enabled=1";
                found = true;
                break;
            }
        }
        if (!found) lines.Add("vr-enabled=1");
        File.WriteAllLines(BootConfig, lines);
        Log("boot.config patched.");
    }

    private void Log(string message)
    {
        logBox?.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
    }

    private void SaveSettings()
    {
        try
        {
            var settings = new Dictionary<string, object>
            {
                ["TurnSpeed"] = (double)turnSpeedInput.Value,
                ["VRScale"] = (double)vrScaleInput.Value,
                ["LocalHands"] = localHandsCheck.Checked,
                ["Fly"] = flyCheck.Checked,
                ["FlySpeed"] = (double)flySpeedInput.Value
            };
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return;
            var doc = JsonDocument.Parse(File.ReadAllText(SettingsFile));
            var root = doc.RootElement;

            syncing = true;
            if (root.TryGetProperty("TurnSpeed", out var ts))
                turnSpeedInput.Value = Math.Clamp((decimal)ts.GetDouble(), turnSpeedInput.Minimum, turnSpeedInput.Maximum);
            if (root.TryGetProperty("VRScale", out var vs))
                vrScaleInput.Value = Math.Clamp((decimal)vs.GetDouble(), vrScaleInput.Minimum, vrScaleInput.Maximum);
            if (root.TryGetProperty("LocalHands", out var lh))
                localHandsCheck.Checked = lh.GetBoolean();
            if (root.TryGetProperty("Fly", out var fly))
                flyCheck.Checked = fly.GetBoolean();
            if (root.TryGetProperty("FlySpeed", out var fsp))
                flySpeedInput.Value = Math.Clamp((decimal)fsp.GetDouble(), flySpeedInput.Minimum, flySpeedInput.Maximum);
            syncing = false;

            Log("Settings loaded.");
        }
        catch { syncing = false; }
    }
}
