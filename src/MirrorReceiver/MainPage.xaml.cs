using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.Miracast;
using Windows.Media.Playback;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace MirrorReceiver
{
    /// <summary>
    /// Miracast receiver (sink) page.
    ///
    /// Session event handlers are written as lambdas so the compiler infers the
    /// event-args types from the event itself. This is deliberate: the Microsoft
    /// Learn pages for Windows.Media.Miracast are stubs with no member list, so
    /// spelling the arg types out by hand would be guesswork. Lambda inference is
    /// what the known-good reference sample uses.
    ///
    /// The receiver is small because Windows owns the hard parts: Wi-Fi Direct
    /// discovery, the Miracast control protocol, RTP depacketisation, H.264/H.265
    /// decode and audio. You get a MiracastReceiverMediaSource that is directly
    /// assignable to MediaPlayer.Source.
    ///
    /// CONSTRAINT: Windows.Media.Miracast only works from a packaged UWP app with
    /// the capabilities declared in Package.appxmanifest. It cannot be called from
    /// Win32, WPF or a console app.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private MiracastReceiver _receiver;
        private MiracastReceiverSession _session;
        private MediaPlayer _mediaPlayer;
        private readonly StringBuilder _log = new StringBuilder();
        private bool _isFullScreen;
        private string _friendlyName = "Mirror Receiver";

        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += OnLoaded;
            this.Unloaded += OnUnloaded;
        }

        // ------------------------------------------------------------------ //
        // Lifecycle
        // ------------------------------------------------------------------ //

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Window.Current.CoreWindow.KeyDown += OnKeyDown;
            await StartReceiverAsync();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try { Window.Current.CoreWindow.KeyDown -= OnKeyDown; } catch { /* window gone */ }
            TearDownMediaPlayer();
            TearDownSession();
        }

        // ------------------------------------------------------------------ //
        // Receiver bootstrapping
        // ------------------------------------------------------------------ //

        private async Task StartReceiverAsync()
        {
            SetStatus("Starting receiver...");

            _receiver = new MiracastReceiver();
            _receiver.StatusChanged += OnReceiverStatusChanged;

            // 1. Configure and apply settings.
            MiracastReceiverSettings settings = _receiver.GetDefaultSettings();
            _friendlyName = ResolveFriendlyName();
            settings.FriendlyName = _friendlyName;
            settings.AuthorizationMethod = MiracastReceiverAuthorizationMethod.None;
            settings.RequireAuthorizationFromKnownTransmitters = false;

            MiracastReceiverApplySettingsResult apply =
                await _receiver.DisconnectAllAndApplySettingsAsync(settings);
            Log($"ApplySettings -> {apply.Status}");
            NameBox.Text = _friendlyName;

            // 2. Create the session. null selects the main application view.
            _session = await _receiver.CreateSessionAsync(null);
            if (_session == null)
            {
                SetStatus("Failed to create a Miracast session.");
                return;
            }

            _session.AllowConnectionTakeover = AllowTakeoverCheck.IsChecked == true;

            _session.ConnectionCreated += (session, args) =>
            {
                // args.Connection.Transmitter.Name is the phone's device name.
                string name = args.Connection.Transmitter.Name;
                Log($"ConnectionCreated: {name}");
                RunOnUi(() => SetStatus($"Connected to {name}"));
            };

            _session.Disconnected += (session, args) =>
            {
                string name = args.Connection.Transmitter.Name;
                Log($"Disconnected: {name}");
                RunOnUi(() =>
                {
                    TearDownMediaPlayer();
                    ShowIdleChrome();
                    SetStatus("Disconnected. Waiting for a phone to connect.");
                });
            };

            _session.MediaSourceCreated += (session, args) =>
            {
                Log($"MediaSourceCreated: {args.MediaSource?.Uri}");
                RunOnUi(() => AttachMediaSource(args.MediaSource));
            };

            // 3. Start listening (advertises over Wi-Fi Direct).
            MiracastReceiverSessionStartResult start = await _session.StartAsync();
            Log($"Session.Start -> {start.Status}");

            MiracastReceiverStatus status = _receiver.GetStatus();
            DetailText.Text = $"ListeningStatus = {status.ListeningStatus}";
            SetStatus($"Listening as \"{_friendlyName}\" - waiting for a phone to connect.");
        }

        private string ResolveFriendlyName()
        {
            string typed = (NameBox.Text ?? string.Empty).Trim();
            if (typed.Length > 0) return typed;
            // Default to the machine name: that is what users recognise on the phone.
            try { return Environment.MachineName; } catch { return "Mirror Receiver"; }
        }

        // ------------------------------------------------------------------ //
        // Media
        // ------------------------------------------------------------------ //

        private void AttachMediaSource(object mediaSource)
        {
            TearDownMediaPlayer();

            _mediaPlayer = new MediaPlayer
            {
                AutoPlay = false,
                RealTimePlayback = true   // latency over buffering: this is a live stream
            };

            // The OS-provided Miracast media source is directly assignable.
            _mediaPlayer.Source = mediaSource as IMediaPlaybackSource;

            _mediaPlayer.MediaOpened += (s, e) => Log("MediaPlayer opened");
            _mediaPlayer.MediaFailed += (s, e) =>
            {
                Log($"MediaPlayer FAILED: {e.ErrorMessage}");
                RunOnUi(() => SetStatus($"Decode failed: {e.ErrorMessage}"));
            };

            VideoSurface.SetMediaPlayer(_mediaPlayer);
            _mediaPlayer.Play();

            ShowVideoChrome();
            SetStatus("Mirroring");
            EnterFullScreen();
        }

        private void TearDownMediaPlayer()
        {
            try
            {
                VideoSurface.SetMediaPlayer(null);
                if (_mediaPlayer != null)
                {
                    try { _mediaPlayer.Pause(); } catch { }
                    _mediaPlayer.Dispose();
                }
            }
            catch (Exception ex) { Log($"TearDownMediaPlayer: {ex.Message}"); }
            finally { _mediaPlayer = null; }
        }

        // ------------------------------------------------------------------ //
        // Events
        // ------------------------------------------------------------------ //

        private void OnReceiverStatusChanged(MiracastReceiver sender, object args)
        {
            try
            {
                MiracastReceiverStatus s = sender.GetStatus();
                Log($"StatusChanged -> {s.ListeningStatus}");
                RunOnUi(() => DetailText.Text = $"ListeningStatus = {s.ListeningStatus}");
            }
            catch (Exception ex) { Log($"StatusChanged error: {ex.Message}"); }
        }

        private void OnKeyDown(CoreWindow sender, KeyEventArgs args)
        {
            switch (args.VirtualKey)
            {
                case VirtualKey.Escape:
                    if (_isFullScreen) { ExitFullScreen(); args.Handled = true; }
                    break;
                case VirtualKey.F:
                    ToggleFullScreen();
                    args.Handled = true;
                    break;
            }
        }

        // ------------------------------------------------------------------ //
        // UI actions
        // ------------------------------------------------------------------ //

        private async void OnApplySettings(object sender, RoutedEventArgs e)
        {
            if (_receiver == null) return;
            try
            {
                MiracastReceiverSettings settings = _receiver.GetDefaultSettings();
                _friendlyName = ResolveFriendlyName();
                settings.FriendlyName = _friendlyName;
                settings.AuthorizationMethod = MiracastReceiverAuthorizationMethod.None;
                settings.RequireAuthorizationFromKnownTransmitters = false;

                MiracastReceiverApplySettingsResult r =
                    await _receiver.DisconnectAllAndApplySettingsAsync(settings);
                Log($"Re-applied settings -> {r.Status}");
                SetStatus($"Settings applied. Listening as \"{_friendlyName}\".");
            }
            catch (Exception ex)
            {
                Log($"ApplySettings failed: {ex.Message}");
                SetStatus($"Could not apply settings: {ex.Message}");
            }
        }

        private async void OnRestartService(object sender, RoutedEventArgs e)
        {
            Log("Restart requested");
            SetStatus("Restarting receiver...");
            TearDownMediaPlayer();
            TearDownSession();
            await StartReceiverAsync();
        }

        private void OnToggleFullscreen(object sender, RoutedEventArgs e) => ToggleFullScreen();

        // ------------------------------------------------------------------ //
        // Fullscreen
        // ------------------------------------------------------------------ //

        private void EnterFullScreen()
        {
            try
            {
                if (ApplicationView.GetForCurrentView().TryEnterFullScreenMode())
                    _isFullScreen = true;
            }
            catch (Exception ex) { Log($"EnterFullScreen: {ex.Message}"); }
        }

        private void ExitFullScreen()
        {
            try
            {
                ApplicationView.GetForCurrentView().ExitFullScreenMode();
                _isFullScreen = false;
            }
            catch (Exception ex) { Log($"ExitFullScreen: {ex.Message}"); }
        }

        private void ToggleFullScreen()
        {
            if (_isFullScreen) ExitFullScreen(); else EnterFullScreen();
        }

        // ------------------------------------------------------------------ //
        // Chrome visibility
        // ------------------------------------------------------------------ //

        private void ShowIdleChrome()
        {
            IdlePanel.Visibility = Visibility.Visible;
            LogPanel.Visibility = Visibility.Visible;
            ConnectedHint.Visibility = Visibility.Collapsed;
        }

        private void ShowVideoChrome()
        {
            IdlePanel.Visibility = Visibility.Collapsed;
            LogPanel.Visibility = Visibility.Collapsed;
            ConnectedHint.Visibility = Visibility.Visible;
        }

        // ------------------------------------------------------------------ //
        // Teardown
        // ------------------------------------------------------------------ //

        private void TearDownSession()
        {
            try
            {
                // Disposing the session retires its event subscriptions.
                _session?.Dispose();
            }
            catch (Exception ex) { Log($"TearDownSession: {ex.Message}"); }
            finally { _session = null; }

            if (_receiver != null)
            {
                try { _receiver.StatusChanged -= OnReceiverStatusChanged; } catch { }
                _receiver = null;
            }
        }

        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        private void SetStatus(string text)
        {
            StatusText.Text = text;
            Log(text);
        }

        private void Log(string message, [CallerMemberName] string caller = null)
        {
            string line = $"{DateTime.Now:HH:mm:ss}  {caller}  {message}";
            Debug.WriteLine("MirrorReceiver::" + line);

            _log.AppendLine(line);
            if (_log.Length > 20000) _log.Remove(0, _log.Length - 12000);  // bounded buffer

            RunOnUi(() =>
            {
                LogText.Text = _log.ToString();
                LogScroll.ChangeView(null, LogScroll.ScrollableHeight, null, true);
            });
        }

        private void RunOnUi(Action action)
        {
            try
            {
                CoreDispatcher d = Dispatcher;
                if (d == null || d.HasThreadAccess) { action(); return; }
                _ = d.RunAsync(CoreDispatcherPriority.Normal, () => action());
            }
            catch { /* shutting down */ }
        }
    }
}
