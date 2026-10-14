// Mira - a small desktop companion that starts and manages the Windows
// Miracast receiver.
//
// Why this exists rather than a full receiver in this window:
// Windows.Media.Miracast is only callable from a packaged UWP app, so a plain
// WinForms process cannot render the mirrored video itself. What a plain
// process CAN do is drive the receiver Windows already ships: start it, watch
// its state, help with first-time setup. That is what this app does.
//
// Compiled by scripts/build-app.ps1 using the C# compiler built into Windows.
// No SDK, no NuGet, no Visual Studio.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Mira
{
    internal static class Program
    {
        public const string AppName = "Mira";
        public const string Version = "1.0.0";

        // The packaged identity of Windows' built-in receiver ("Connect").
        public const string ReceiverAumid =
            "Microsoft.PPIProjection_cw5n1h2txyewy!Microsoft.PPIProjection";
        public const string ReceiverCapability =
            "App.WirelessDisplay.Connect~~~~0.0.1.0";

        [STAThread]
        private static void Main()
        {
            // Only one copy running.
            bool createdNew;
            using (var mutex = new Mutex(true, "Mira.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("Mira is already running - look for it in the tray.",
                        AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private const string Pink = "#7C3AED";

        private readonly Label _status = new Label();
        private readonly Panel _dot = new Panel();
        private readonly Button _start = new Button();
        private readonly Button _help = new Button();
        private readonly Button _install = new Button();
        private readonly NotifyIcon _tray = new NotifyIcon();
        private readonly System.Windows.Forms.Timer _poll = new System.Windows.Forms.Timer();
        private bool _reallyExit;

        public MainForm()
        {
            BuildUi();
            BuildTray();

            // If the feature is missing, surface that immediately.
            if (!ReceiverInstalled())
            {
                SetStatus("Windows' receiver feature is not installed yet.", false);
                _install.Visible = true;
            }
            else
            {
                SetStatus("Ready. Click Start receiver.", false);
            }

            _poll.Interval = 1500;
            _poll.Tick += (s, e) => RefreshState();
            _poll.Start();
        }

        // ------------------------------------------------------------------ //
        // UI
        // ------------------------------------------------------------------ //
        private void BuildUi()
        {
            Text = Program.AppName + " - Miracast receiver";
            ClientSize = new Size(560, 400);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(24, 20, 40);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);

            var title = new Label
            {
                Text = Program.AppName,
                Font = new Font("Segoe UI Semibold", 22f),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(28, 24)
            };

            var subtitle = new Label
            {
                Text = "Mirror your Android phone to this PC",
                Font = new Font("Segoe UI", 10f),
                ForeColor = Color.FromArgb(170, 170, 190),
                AutoSize = true,
                Location = new Point(31, 66)
            };

            // Status row: coloured dot + text
            _dot.Size = new Size(12, 12);
            _dot.Location = new Point(31, 108);
            _dot.BackColor = Color.FromArgb(120, 120, 140);

            _status.AutoSize = false;
            _status.Size = new Size(480, 22);
            _status.Location = new Point(52, 104);
            _status.ForeColor = Color.FromArgb(200, 200, 215);

            _start.Text = "Start receiver";
            _start.Size = new Size(200, 46);
            _start.Location = new Point(28, 148);
            _start.FlatStyle = FlatStyle.Flat;
            _start.FlatAppearance.BorderSize = 0;
            _start.BackColor = ColorTranslator.FromHtml(Pink);
            _start.ForeColor = Color.White;
            _start.Font = new Font("Segoe UI Semibold", 11f);
            _start.Cursor = Cursors.Hand;
            _start.Click += (s, e) => StartReceiver();

            _help.Text = "How to connect";
            _help.Size = new Size(150, 46);
            _help.Location = new Point(240, 148);
            _help.FlatStyle = FlatStyle.Flat;
            _help.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 95);
            _help.BackColor = Color.FromArgb(38, 34, 58);
            _help.ForeColor = Color.White;
            _help.Cursor = Cursors.Hand;
            _help.Click += (s, e) => ShowHelp();

            _install.Text = "Install receiver feature (needs admin)";
            _install.Size = new Size(300, 32);
            _install.Location = new Point(28, 206);
            _install.FlatStyle = FlatStyle.Flat;
            _install.FlatAppearance.BorderColor = Color.FromArgb(90, 70, 40);
            _install.BackColor = Color.FromArgb(46, 38, 26);
            _install.ForeColor = Color.FromArgb(240, 200, 140);
            _install.Cursor = Cursors.Hand;
            _install.Click += (s, e) => InstallFeature();

            var help = new Label
            {
                Location = new Point(28, 256),
                Size = new Size(505, 120),
                ForeColor = Color.FromArgb(150, 150, 170),
                Text =
                    "On your Android phone:\r\n" +
                    "    Settings  >  Connections  >  Cast / Smart View /\r\n" +
                    "    Wireless display / Screen mirroring\r\n\r\n" +
                    "Then pick this PC from the list.\r\n\r\n" +
                    "Both devices must be on the same Wi-Fi network."
            };

            Controls.AddRange(new Control[]
            {
                title, subtitle, _dot, _status, _start, _help, _install, help
            });

            // WinForms recalculates child visibility when a control joins the
            // parent, so hiding _install must happen AFTER AddRange. Setting it
            // before has no effect and the button shows up when it shouldn't.
            _install.Visible = false;

            Resize += (s, e) => { if (WindowState == FormWindowState.Minimized) HideToTray(); };
            FormClosing += OnClosing;
        }

        private void BuildTray()
        {
            Icon icon;
            try { icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { icon = SystemIcons.Application; }

            _tray.Icon = icon ?? SystemIcons.Application;
            _tray.Text = Program.AppName + " - Miracast receiver";
            _tray.Visible = true;
            _tray.DoubleClick += (s, e) => ShowFromTray();

            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Mira", null, (s, e) => ShowFromTray());
            menu.Items.Add("Start receiver", null, (s, e) => StartReceiver());
            menu.Items.Add("How to connect", null, (s, e) => ShowHelp());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => { _reallyExit = true; Close(); });
            _tray.ContextMenuStrip = menu;
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
            {
                // Closing the window keeps it in the tray by default.
                e.Cancel = true;
                HideToTray();
                return;
            }
            _tray.Visible = false;
        }

        private void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;
            _tray.ShowBalloonTip(2000, Program.AppName,
                "Still running in the tray. Right-click the icon to exit.", ToolTipIcon.Info);
        }

        private void ShowFromTray()
        {
            Show();
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            Activate();
        }

        // ------------------------------------------------------------------ //
        // Behaviour
        // ------------------------------------------------------------------ //
        private void SetStatus(string text, bool running)
        {
            _status.Text = text;
            _dot.BackColor = running
                ? Color.FromArgb(74, 222, 128)     // green
                : Color.FromArgb(140, 140, 165);   // grey
        }

        private void RefreshState()
        {
            bool running = ReceiverRunning();
            if (running)
                SetStatus("Receiver is running. Now connect from your phone.", true);
            else if (ReceiverInstalled())
                SetStatus("Ready. Click Start receiver.", false);
        }

        private static bool ReceiverInstalled()
        {
            // The Connect app registers a Start-menu entry under this AUMID.
            // If the appx package is absent the shell launch simply fails, so
            // a cheap presence check is to look for its package folder.
            try
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                foreach (var dir in System.IO.Directory.GetDirectories(
                             System.IO.Path.Combine(root, "SystemApps")))
                {
                    if (dir.IndexOf("PPIProjection", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { /* access denied is expected for SystemApps */ }

            // Fall back: assume installed and let the launch attempt decide.
            return true;
        }

        private static bool ReceiverRunning()
        {
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.ProcessName.IndexOf("PPIProjection", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                        if (p.ProcessName.IndexOf("WirelessDisplay", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;

                        string t = p.MainWindowTitle;
                        if (!string.IsNullOrEmpty(t) &&
                            t.IndexOf("Connect", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                    catch { }
                }
            }
            catch { }
            return false;
        }

        private void StartReceiver()
        {
            if (ReceiverRunning())
            {
                SetStatus("Receiver is already running.", true);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(
                    "explorer.exe",
                    "shell:appsFolder\\" + Program.ReceiverAumid)
                { UseShellExecute = true });

                SetStatus("Starting receiver...", false);
                _install.Visible = false;

                var settle = new System.Windows.Forms.Timer { Interval = 3000 };
                settle.Tick += (s, e) =>
                {
                    settle.Stop();
                    settle.Dispose();
                    if (ReceiverRunning())
                        SetStatus("Receiver is running. Now connect from your phone.", true);
                    else
                    {
                        SetStatus("Could not start the receiver feature.", false);
                        _install.Visible = true;
                    }
                };
                settle.Start();
            }
            catch (Exception ex)
            {
                SetStatus("Launch failed: " + ex.Message, false);
                _install.Visible = true;
            }
        }

        private void InstallFeature()
        {
            string inner =
                "Add-WindowsCapability -Online -Name '" + Program.ReceiverCapability + "'; " +
                "Write-Host ''; Write-Host 'Done. You may need to restart the PC.'; " +
                "Read-Host 'Press Enter to close'";

            try
            {
                Process.Start(new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -Command \"" + inner + "\"")
                {
                    UseShellExecute = true,
                    Verb = "runas"      // triggers UAC
                });

                SetStatus("Installer launched - approve the UAC prompt.", false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not start the installer: " + ex.Message + "\r\n\r\n" +
                    "Run this in an Administrator PowerShell instead:\r\n\r\n" +
                    "Add-WindowsCapability -Online -Name " + Program.ReceiverCapability,
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowHelp()
        {
            MessageBox.Show(
                "1.  Click \"Start receiver\".\r\n\r\n" +
                "2.  On your Android phone open:\r\n" +
                "        Settings > Connections > Cast\r\n" +
                "        (also called Smart View, Wireless display,\r\n" +
                "         or Screen mirroring depending on the brand)\r\n\r\n" +
                "3.  Pick this PC from the list.\r\n\r\n" +
                "If the PC does not appear:\r\n" +
                "  - both devices must be on the same Wi-Fi network\r\n" +
                "  - Settings > System > Projecting to this PC\r\n" +
                "    set both dropdowns to\r\n" +
                "    \"Available everywhere on secure networks\"\r\n" +
                "  - some phones have dropped Miracast entirely\r\n" +
                "    (Google Pixel, for example)",
                "How to connect", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
                if (_poll != null) { _poll.Dispose(); }
            }
            base.Dispose(disposing);
        }
    }
}
