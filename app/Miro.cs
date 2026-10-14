// Miro - a small desktop companion that starts and manages the Windows
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

namespace Miro
{
    internal static class Program
    {
        public const string AppName = "Miro";
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
            using (var mutex = new Mutex(true, "Miro.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("Miro is already running - look for it in the tray.",
                        AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }

    /// <summary>
    /// Activates a packaged (UWP) app by its Application User Model ID.
    ///
    /// This uses IApplicationActivationManager, the documented way for a desktop
    /// process to launch a UWP app. It is used instead of
    /// "explorer.exe shell:appsFolder\..." because that route spawns a fresh
    /// explorer.exe, which can fail with 0xc0000142 (STATUS_DLL_INIT_FAILED)
    /// when the calling process has an unusual token or sits in a job object.
    /// The COM path has no such dependency.
    /// </summary>
    internal static class UwpLauncher
    {
        [ComImport]
        [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IApplicationActivationManager
        {
            [PreserveSig]
            int ActivateApplication(
                [In] string appUserModelId,
                [In] string arguments,
                [In] uint options,
                [Out] out uint processId);

            [PreserveSig]
            int ActivateForFile(
                [In] string appUserModelId,
                [In] IntPtr itemArray,
                [In] string verb,
                [Out] out uint processId);

            [PreserveSig]
            int ActivateForProtocol(
                [In] string appUserModelId,
                [In] IntPtr itemArray,
                [Out] out uint processId);
        }

        private static readonly Guid Clsid =
            new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C");

        /// <summary>
        /// Returns the new process id, or 0 on failure (with a reason in error).
        /// </summary>
        public static uint Activate(string aumid, out string error)
        {
            error = null;
            try
            {
                Type t = Type.GetTypeFromCLSID(Clsid);
                if (t == null)
                {
                    error = "ApplicationActivationManager is not registered.";
                    return 0;
                }

                var mgr = (IApplicationActivationManager)Activator.CreateInstance(t);
                uint pid;
                int hr = mgr.ActivateApplication(aumid, null, 0, out pid);
                if (hr < 0)
                {
                    error = "ActivateApplication failed (HRESULT 0x" + hr.ToString("X8") + ")";
                    return 0;
                }
                return pid;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return 0;
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
        private uint _launchedPid;

        public MainForm()
        {
            BuildUi();
            BuildTray();

            // Deliberately do NOT probe for the feature up front. Detecting it
            // reliably requires elevation, and guessing wrong hides a working
            // setup behind an install button. It only appears if a launch fails.
            _install.Visible = false;

            // Detect a sandboxed/restricted launch, where the process cannot
            // create child processes. Everything Miro does (COM activation of
            // the receiver, launching it) fails there, with confusing errors.
            // Better to say so up front than to fail on the button press.
            string spawn = SpawnTest();
            WriteDiagnostic("startup (version " + Program.Version + ")");

            if (spawn.IndexOf("children WORK", StringComparison.Ordinal) < 0)
            {
                SetStatus("Restricted launch detected - close Miro and start it " +
                          "from File Explorer, not from a sandboxed shell.", false);
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
            menu.Items.Add("Open Miro", null, (s, e) => ShowFromTray());
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
            bool running = ReceiverRunning() || ProcessAlive(_launchedPid);
            if (running)
                SetStatus("Receiver is running. Now connect from your phone.", true);
        }

        /// <summary>
        /// Records how Miro was started and whether it can create a working
        /// child process at all.
        ///
        /// The Miracast/UWP activation APIs need the interactive window station
        /// and desktop. A process started by an agent, service or harness can
        /// end up elsewhere, which produces exactly the symptoms seen here:
        /// E_ACCESSDENIED from COM activation plus 0xc0000142 in children.
        /// </summary>
        private static void WriteDiagnostic(string note)
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string path = System.IO.Path.Combine(dir, "miro-diagnostic.txt");

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
                sb.AppendLine(note);
                sb.AppendLine(DescribeContext());
                sb.AppendLine("Parent     : " + DescribeParent());
                sb.AppendLine("Spawn test : " + SpawnTest());
                sb.AppendLine("Exe        : " + Application.ExecutablePath);
                sb.AppendLine("SystemRoot : " + (Environment.GetEnvironmentVariable("SystemRoot") ?? "(null)"));
                sb.AppendLine("Temp       : " + System.IO.Path.GetTempPath());

                System.IO.File.AppendAllText(path, sb.ToString() + Environment.NewLine);
            }
            catch { /* diagnostics must never break the app */ }
        }

        private static string DescribeParent()
        {
            try
            {
                int me = Process.GetCurrentProcess().Id;
                using (var q = new System.Management.ManagementObjectSearcher(
                    "SELECT ParentProcessId FROM Win32_Process WHERE ProcessId=" + me))
                {
                    foreach (System.Management.ManagementBaseObject mo in q.Get())
                    {
                        int parent = Convert.ToInt32(mo["ParentProcessId"]);
                        try
                        {
                            using (var p = Process.GetProcessById(parent))
                                return p.ProcessName + " (pid " + parent + ")";
                        }
                        catch { return "pid " + parent + " (name unavailable)"; }
                    }
                }
            }
            catch (Exception ex) { return "(lookup failed: " + ex.Message + ")"; }

            return "(not found)";
        }

        /// <summary>
        /// Runs a tiny child process and checks it actually produced output.
        /// Process.Start succeeding is not proof of anything: the child can be
        /// created and then die during initialisation.
        /// </summary>
        private static string SpawnTest()
        {
            string probe = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "miro-spawn-probe.txt");

            try { if (System.IO.File.Exists(probe)) System.IO.File.Delete(probe); }
            catch { }

            try
            {
                var psi = new ProcessStartInfo("cmd.exe",
                    "/c echo probe > \"" + probe + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var p = Process.Start(psi))
                {
                    if (!p.WaitForExit(5000)) return "child hung (no exit in 5s)";
                    bool wrote = System.IO.File.Exists(probe);
                    return "cmd.exe exit=" + p.ExitCode + " wroteFile=" + wrote +
                           (wrote ? " -> children WORK" : " -> child died/failed");
                }
            }
            catch (Exception ex)
            {
                return "Process.Start threw: " + ex.Message;
            }
        }

        /// <summary>
        /// Describes the security context Miro is running in. The Miracast/UWP
        /// activation APIs are refused for elevated and restricted tokens, so
        /// this is the first thing worth knowing when a launch fails.
        /// </summary>
        private static string DescribeContext()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                sb.AppendLine("User      : " + id.Name);

                var principal = new System.Security.Principal.WindowsPrincipal(id);
                bool admin = principal.IsInRole(
                    System.Security.Principal.WindowsBuiltInRole.Administrator);
                sb.AppendLine("Elevated  : " + admin);
            }
            catch (Exception ex)
            {
                sb.AppendLine("User      : (unavailable: " + ex.Message + ")");
            }

            sb.AppendLine("64-bit    : " + Environment.Is64BitProcess);
            try { sb.AppendLine("Session   : " + Process.GetCurrentProcess().SessionId); }
            catch { }
            return sb.ToString().TrimEnd();
        }

        private static bool ProcessAlive(uint pid)
        {
            if (pid == 0) return false;
            try
            {
                using (var p = Process.GetProcessById((int)pid))
                    return !p.HasExited;
            }
            catch { return false; }
        }

        // Note: there is deliberately no ReceiverInstalled() probe. Checking for
        // the Wireless Display package needs elevation, and a wrong answer would
        // hide a working setup behind an "install" button. Whether the feature
        // exists is decided by actually trying to launch it.

        private static bool ReceiverRunning()
        {
            // Process objects hold native handles and MUST be disposed. This runs
            // on a 1.5s timer, so leaking them exhausts the process handle table
            // and the desktop heap -- which makes CreateProcess fail for children
            // with 0xc0000142 (STATUS_DLL_INIT_FAILED), exactly the symptom this
            // was written to diagnose.
            Process[] all;
            try { all = Process.GetProcesses(); }
            catch { return false; }

            try
            {
                foreach (var p in all)
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
            finally
            {
                foreach (var p in all)
                {
                    try { p.Dispose(); } catch { }
                }
            }
            return false;
        }

        private void StartReceiver()
        {
            if (ReceiverRunning() || ProcessAlive(_launchedPid))
            {
                SetStatus("Receiver is already running.", true);
                return;
            }

            SetStatus("Starting receiver...", false);

            // Primary: COM activation of the packaged app. No explorer involved.
            string comError;
            uint pid = UwpLauncher.Activate(Program.ReceiverAumid, out comError);

            // Fallback: shell activation. Keep comError for the report below --
            // if this route also fails, knowing WHY the COM route failed is the
            // useful part.
            string shellError = null;
            if (pid == 0)
            {
                // Fallback 1: hand the shell: URI directly to ShellExecuteEx.
                // This asks the shell to resolve the moniker instead of spawning
                // our own explorer.exe, which is what dies with 0xc0000142.
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "shell:appsFolder\\" + Program.ReceiverAumid,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex1)
                {
                    shellError = "shell uri: " + ex1.Message;

                    // Fallback 2: explicit explorer.exe.
                    try
                    {
                        Process.Start(new ProcessStartInfo(
                            "explorer.exe",
                            "shell:appsFolder\\" + Program.ReceiverAumid)
                        { UseShellExecute = true });
                        shellError = null;
                    }
                    catch (Exception ex2)
                    {
                        shellError += " / explorer: " + ex2.Message;
                    }
                }
            }

            _launchedPid = pid;
            _install.Visible = false;

            var settle = new System.Windows.Forms.Timer { Interval = 3000 };
            settle.Tick += (s, e) =>
            {
                settle.Stop();
                settle.Dispose();

                if (ReceiverRunning() || ProcessAlive(_launchedPid))
                {
                    SetStatus("Receiver is running. Now connect from your phone.", true);
                    return;
                }

                string why = comError ?? "unknown";
                if (shellError != null) why += " / shell: " + shellError;
                SetStatus("Receiver did not start", false);
                _install.Visible = true;

                WriteDiagnostic(
                    "launch FAILED" + Environment.NewLine +
                    "  comError   = " + (comError ?? "(none)") + Environment.NewLine +
                    "  shellError = " + (shellError ?? "(none)") + Environment.NewLine +
                    "  pid        = " + pid);

                MessageBox.Show(
                    "Could not start the Windows receiver.\r\n\r\n" +
                    "COM activation : " + (comError ?? "ok") + "\r\n" +
                    "Shell fallback : " + (shellError ?? "launched") + "\r\n\r\n" +
                    "--- how Miro is running ---\r\n" +
                    DescribeContext() + "\r\n" +
                    "Windows refuses to activate a packaged app from an elevated or\r\n" +
                    "restricted process (E_ACCESSDENIED), and child processes from\r\n" +
                    "such a process fail with 0xc0000142. If 'Elevated' above says\r\n" +
                    "True, close Miro and start it from a normal (non-admin) Explorer.",
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            settle.Start();
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
