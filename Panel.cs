using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class Program
{
    internal const string Url = "http://127.0.0.1:1140/";
    internal static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaydayTrainerPanel");
    internal static bool Allowed(string url)
    {
        Uri uri;
        return Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == "http" &&
            uri.Host == "127.0.0.1" && uri.Port == 1140 && String.IsNullOrEmpty(uri.UserInfo);
    }
    internal static void Log(string value)
    {
        try { Directory.CreateDirectory(DataDir); File.AppendAllText(Path.Combine(DataDir, "panel.log"), DateTime.Now.ToString("s") + " " + value + Environment.NewLine); } catch { }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test"))
        {
            bool ok = Allowed(Url) && Allowed(Url + "mission") &&
                !Allowed("http://127.0.0.1:1140.evil.test/") && !Allowed("http://localhost:1140/") &&
                !Allowed("https://127.0.0.1:1140/") && !Allowed("file:///C:/Windows/") &&
                !Allowed("http://user@127.0.0.1:1140/") && !Allowed("http://127.0.0.1:1139/");
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test.txt"), ok ? "PASS: local navigation boundary" : "FAIL");
            return ok ? 0 : 1;
        }
        bool created;
        using (var mutex = new Mutex(true, args.Contains("--smoke") ? "Local\\PaydayTrainerPanelSmoke" : "Local\\PaydayTrainerPanelF8", out created))
        {
            if (!created) { MessageBox.Show("O painel já está aberto. Use F8 no PAYDAY 2 ou o ícone perto do relógio.", "Painel PAYDAY"); return 0; }
            Directory.CreateDirectory(DataDir);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Log(e.Exception.ToString()); };
            Application.Run(new TrainerPanel(args.Contains("--smoke")));
        }
        return 0;
    }
}

internal sealed class TrainerPanel : Form
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flag);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    private readonly WebView2 web = new WebView2();
    private readonly Label status = new Label();
    private readonly NotifyIcon tray = new NotifyIcon();
    private readonly System.Windows.Forms.Timer focusTimer = new System.Windows.Forms.Timer();
    private readonly bool smoke;
    private bool quitting, ready, hotkey, smokeStarted;
    private DateTime shownAt;
    private IntPtr gameWindow;
    private readonly string smokePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smoke-result.txt");

    public TrainerPanel(bool test)
    {
        smoke = test;
        Text = "PAYDAY 2 — Ultimate Trainer — F8 abre / fecha";
        BackColor = Color.FromArgb(16, 23, 31);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimumSize = new Size(760, 500);
        Size = new Size(1120, 780);
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = !smoke;
        ShowInTaskbar = true;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6), BackColor = Color.FromArgb(30, 49, 67), WrapContents = false };
        AddButton(bar, "Ocultar · F8", delegate { HidePanel(true); });
        AddButton(bar, "Recarregar", delegate { if (ready) web.CoreWebView2.Navigate(Program.Url); });
        AddButton(bar, "Encerrar painel", delegate { quitting = true; Close(); });
        status.Text = "Use janela sem bordas no jogo. F8 abre e fecha o painel.";
        status.AutoSize = true; status.Margin = new Padding(10, 7, 0, 0);
        bar.Controls.Add(status);
        web.Dock = DockStyle.Fill;
        web.DefaultBackgroundColor = BackColor;
        Controls.Add(web); Controls.Add(bar);
        tray.Icon = SystemIcons.Application;
        tray.Text = "PAYDAY Trainer — F8";
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir painel (F8)", null, delegate { ShowPanel(); });
        menu.Items.Add("Encerrar painel", null, delegate { quitting = true; Close(); });
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += delegate { ShowPanel(); };
        tray.Visible = !smoke;
        focusTimer.Interval = 350;
        focusTimer.Tick += delegate
        {
            if (smoke || !Visible || (DateTime.Now - shownAt).TotalSeconds < 1.5) return;
            IntPtr current = GetForegroundWindow();
            if (!IsPanel(current) && current != IntPtr.Zero) HidePanel(false);
        };
        Shown += async delegate { shownAt = DateTime.Now; await Initialize(); };
        FormClosing += delegate(object sender, FormClosingEventArgs e)
        {
            if (!quitting && !smoke && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HidePanel(true); }
        };
    }

    private void AddButton(Control parent, string label, EventHandler action)
    {
        var button = new Button { Text = label, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 71, 96), ForeColor = Color.White, Height = 30 };
        button.Click += action;
        parent.Controls.Add(button);
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!smoke)
        {
            hotkey = RegisterHotKey(Handle, 8, 0x4000, (uint)Keys.F8);
            if (!hotkey) { Program.Log("F8 is occupied."); MessageBox.Show("F8 já está em uso por outro programa. O painel pode ser aberto pelo ícone perto do relógio.", "Painel PAYDAY"); }
        }
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (hotkey) UnregisterHotKey(Handle, 8);
        hotkey = false;
        base.OnHandleDestroyed(e);
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        focusTimer.Stop(); focusTimer.Dispose(); tray.Visible = false; tray.Dispose(); web.Dispose();
        base.OnFormClosed(e);
    }
    private bool IsPanel(IntPtr hwnd)
    {
        return hwnd == Handle || GetAncestor(hwnd, 2) == Handle || GetAncestor(hwnd, 3) == Handle;
    }
    private bool IsGame(IntPtr hwnd)
    {
        uint processId;
        GetWindowThreadProcessId(hwnd, out processId);
        try
        {
            using (var process = Process.GetProcessById((int)processId))
                return new[] { "PAYDAY2", "payday2_win32_release", "payday2_win64_release" }.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && m.WParam.ToInt32() == 8)
        {
            IntPtr foreground = GetForegroundWindow();
            if (Visible && IsPanel(foreground)) HidePanel(true);
            else if (IsGame(foreground) || IsPanel(foreground)) { gameWindow = foreground; ShowPanel(); }
            return;
        }
        base.WndProc(ref m);
    }
    private void ShowPanel()
    {
        IntPtr foreground = GetForegroundWindow();
        if (IsGame(foreground)) gameWindow = foreground;
        Rect rect;
        if (gameWindow != IntPtr.Zero && GetWindowRect(gameWindow, out rect))
        {
            var working = Screen.FromHandle(gameWindow).WorkingArea;
            Size = new Size(Math.Min(1280, Math.Max(760, working.Width - 100)), Math.Min(850, Math.Max(500, working.Height - 100)));
            Location = new Point(working.Left + (working.Width - Width) / 2, working.Top + (working.Height - Height) / 2);
        }
        shownAt = DateTime.Now;
        Show(); WindowState = FormWindowState.Normal; Activate(); web.Focus();
    }
    private void HidePanel(bool restoreGame)
    {
        Hide();
        if (restoreGame && gameWindow != IntPtr.Zero && IsGame(gameWindow)) SetForegroundWindow(gameWindow);
    }

    private async Task Initialize()
    {
        try
        {
            // ponytail: reuse the complete trainer UI; no duplicated gameplay commands or new network server.
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Program.DataDir, smoke ? "TestProfile" : "Profile"));
            await web.EnsureCoreWebView2Async(environment);
            ready = true;
            web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            web.CoreWebView2.Settings.IsZoomControlEnabled = true;
            web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            web.CoreWebView2.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; };
            web.CoreWebView2.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
            {
                if (!Program.Allowed(e.Uri)) { e.Cancel = true; status.Text = "Navegação externa bloqueada neste painel."; }
            };
            web.CoreWebView2.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e)
            {
                e.Handled = true;
                if (Program.Allowed(e.Uri)) { web.CoreWebView2.Navigate(e.Uri); return; }
                Uri link;
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out link) && link.Scheme == "https" &&
                    MessageBox.Show("Abrir este link no navegador externo?\n" + link.Host, "Link externo", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
            };
            web.CoreWebView2.ProcessFailed += delegate(object sender, CoreWebView2ProcessFailedEventArgs e) { status.Text = "O painel parou. Use Recarregar ou abra novamente."; Program.Log(e.ProcessFailedKind.ToString()); };
            web.CoreWebView2.NavigationCompleted += async delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (!e.IsSuccess)
                {
                    status.Text = "Abra o PAYDAY e o trainer; depois clique em Recarregar.";
                    Program.Log("Navigation failed: " + e.WebErrorStatus);
                    if (smoke) { File.WriteAllText(smokePath, "FAIL: " + e.WebErrorStatus); quitting = true; Close(); }
                    return;
                }
                status.Text = "Todas as abas do trainer · F8 volta ao jogo";
                await web.CoreWebView2.ExecuteScriptAsync(@"(() => {
                    if (document.getElementById('payday-overlay-style')) return;
                    const style = document.createElement('style'); style.id = 'payday-overlay-style';
                    style.textContent = 'html{color-scheme:dark}body{background:#10171f!important}.navbar{position:sticky;top:0;z-index:1020;background:#233c54!important}.navbar .navbar-collapse{display:flex!important;flex-wrap:wrap}.navbar .navbar-toggler{display:none}.navbar .navbar-nav{flex-direction:row!important;flex-wrap:wrap}.navbar .nav-link{padding:6px 10px!important}.navbar .nav-link.active{background:#456d96;border-radius:4px}.card{border-color:#344b61}';
                    document.head.appendChild(style);
                })()");
                await web.CoreWebView2.ExecuteScriptAsync(@"(() => { const host=document.querySelector('#host'); const port=document.querySelector('#port'); if(host && !host.disabled && host.value==='127.0.0.1' && port && port.value==='1138') host.closest('form').requestSubmit(); })()");
                if (smoke && !smokeStarted) { smokeStarted = true; await SmokeTest(); }
            };
            web.CoreWebView2.Navigate(Program.Url);
            if (!smoke) focusTimer.Start();
        }
        catch (Exception e)
        {
            Program.Log(e.ToString()); status.Text = "Não foi possível abrir o painel. Veja panel.log em AppData/Local/PaydayTrainerPanel.";
            if (smoke) { File.WriteAllText(smokePath, "FAIL: " + e); quitting = true; Close(); }
        }
    }
    private async Task SmokeTest()
    {
        try
        {
            bool reserved = RegisterHotKey(Handle, 108, 0x4000, (uint)Keys.F8);
            if (!reserved) throw new Exception("F8 could not be registered; another application may own it.");
            UnregisterHotKey(Handle, 108);
            await Task.Delay(1200);
            // Connecting receives state only; never toggle cheats or send gameplay calls in this test.
            await web.CoreWebView2.ExecuteScriptAsync(@"(() => { const host=document.querySelector('#host'); const port=document.querySelector('#port'); if(host && !host.disabled && host.value==='127.0.0.1' && port && port.value==='1138') host.closest('form').requestSubmit(); })()");
            await Task.Delay(1800);
            var report = new System.Text.StringBuilder();
            report.AppendLine("PASS F8 registration on Windows");
            string[] routes = { "career", "environment", "cheats", "mission", "driving", "spawn", "game", "keybinds", "addons", "settings" };
            foreach (string route in routes)
            {
                string clicked = await web.CoreWebView2.ExecuteScriptAsync("(() => { const a=document.querySelector('a[href=\"/" + route + "\"]'); if(!a) return false; a.click(); return true; })()");
                await Task.Delay(180);
                string location = await web.CoreWebView2.ExecuteScriptAsync("location.pathname");
                if (clicked != "true" || location != "\"/" + route + "\"") throw new Exception("Route failed: " + route + " " + location);
                report.AppendLine("PASS /" + route);
            }
            string build = await web.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('button.nav-link[data-bs-toggle=\"popover\"]')");
            if (build != "true") throw new Exception("Build help missing");
            report.AppendLine("PASS Build help; all routes visited without gameplay actions");
            await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('a[href=\"/mission\"]').click()");
            await Task.Delay(400);
            using (var image = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "panel-preview.png")))
                await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
            File.WriteAllText(smokePath, report.ToString());
        }
        catch (Exception e) { File.WriteAllText(smokePath, "FAIL: " + e); }
        finally { quitting = true; Close(); }
    }
}
