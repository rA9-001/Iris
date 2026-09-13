using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Iris.Interop;
using Iris.Localization;

// System.Windows.Media also defines MediaPlayer; libvlc's is the one we mean.
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace Iris;

public partial class MainWindow : Window
{
    private const double SeekSmallStep = 5_000;   // arrow keys, ms
    private const double SeekLargeStep = 10_000;  // J / L and the skip buttons, ms
    private const int VolumeStep = 5;

    /// <summary>Idle time before the controls fade out while the pointer is over the video.</summary>
    private static readonly TimeSpan ChromeHideDelay = TimeSpan.FromSeconds(1.4);

    private const int ChromeFadeInMs = 120;
    private const int ChromeFadeOutMs = 180;

    /// <summary>Used when the pointer leaves the window: short enough to read as instant,
    /// long enough not to look like a hard pop.</summary>
    private const int ChromeFadeAwayMs = 60;

    /// <summary>Opaque ground shown before any video exists, so the video child window's
    /// unpainted pixels never show through.</summary>
    private static readonly Brush EmptyBackdrop = Frozen(Color.FromRgb(0x07, 0x07, 0x0A));

    /// <summary>Effectively invisible, but enough alpha to keep the layered overlay
    /// hit-testable over the picture.</summary>
    private static readonly Brush MouseCatcher = Frozen(Color.FromArgb(1, 0, 0, 0));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private readonly PlaybackEngine _engine = new();

    /// <summary>Shorthands so the shell reads as if it owned the player directly.</summary>
    private MediaPlayer? _player => _engine.Player;
    private LibVLC? _libVlc => _engine.Vlc;

    private readonly AppSettings _settings;

    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _hideChrome;

    private string? _currentPath;
    private bool _mediaLoaded;
    private bool _endReached;

    /// <summary>libvlc gave up on this file, so there is nothing to drive the transport with.</summary>
    private bool _playbackFailed;

    /// <summary>This file has been seen playing, so an Ended state really is its ending.</summary>
    private bool _startedPlaying;

    /// <summary>
    /// Where frame stepping has walked to, or null when libvlc's own clock is the truth.
    /// libvlc does not move its reported time while stepping, so this carries it.
    /// </summary>
    private double? _steppedMs;


    /// <summary>
    /// Volume and speed still have to be pushed into libvlc. Neither sticks until the
    /// file is actually playing: set any earlier, libvlc has no audio output to apply a
    /// volume to and no input to change the rate of, and silently keeps its own values.
    /// </summary>
    private bool _pendingPlaybackSettings;

    /// <summary>
    /// Playback speed, 1.0 being normal. Kept across files for the session but never
    /// saved: starting a fresh launch at anything but normal speed is a nasty surprise.
    /// </summary>
    private double _rate = 1.0;

    /// <summary>The speeds the button and the shortcuts step through.</summary>
    private static readonly double[] Rates = [0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0];

    private bool _draggingSeek;
    private bool _suppressSeek;
    private bool _suppressVolume;

    private bool _chromeVisible = true;
    private Point _lastMousePos;

    private bool _isFullscreen;
    private WindowState _restoreState;
    private WindowStyle _restoreStyle;
    private ResizeMode _restoreResize;
    private Rect _restoreBounds;
    /// <summary>Exact maximised rectangle to put back when leaving fullscreen.</summary>
    private Native.RectPx _maximizedRect;

    private bool _keepAwake;

    /// <summary>File handed to us on the command line, e.g. by "Open with".</summary>
    private readonly string? _startupPath;

    /// <summary>Playing, but libvlc has not opened a video output yet. The overlay stays
    /// opaque until it does, so the unpainted video window never flashes through.</summary>
    private bool _awaitingVideo;

    /// <summary>We maximised because a file was passed in, not because the user asked.</summary>
    private bool _forcedMaximize;
    private bool _userChangedWindowState;

    /// <summary>Set while Iris itself is driving WindowState, so its own transitions
    /// are not mistaken for the user maximising or restoring the window.</summary>
    private bool _suppressStateTracking;

    private VLCState _tracedState = VLCState.NothingSpecial;

    private bool _revealed;
    private bool _dialogOpen;
    private DispatcherTimer? _revealFallback;

    public MainWindow(string? startupPath = null)
    {
        // Kick the engine off first, on a worker. It takes roughly as long as WPF needs
        // to build and show the window, so running the two in parallel makes it free:
        // by the time there is a window, playback can start immediately.
        _engine.Start();

        _settings = AppSettings.Load();

        // Before InitializeComponent, so the very first layout is already in the right
        // language and no label is ever seen changing.
        Loc.Use(_settings.Language);

        _startupPath = !string.IsNullOrWhiteSpace(startupPath) && File.Exists(startupPath)
            ? startupPath
            : null;

        LaunchTrace.Mark("ctor entry");
        InitializeComponent();
        LaunchTrace.Mark("InitializeComponent");
        RestoreWindowPlacement();

        // Opening a file means the start screen is never the point. Collapsing it here,
        // before the window is shown, is what keeps it from flashing up for a frame.
        if (_startupPath is not null)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            ApplyMediaTitle(_startupPath);
        }

        // Polling beats libvlc's callbacks here: the events fire on internal threads where
        // re-entering the player can deadlock, and 10 Hz on the UI thread costs nothing.
        _tick = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _tick.Tick += OnTick;
        _tick.Start();

        _hideChrome = new DispatcherTimer { Interval = ChromeHideDelay };
        _hideChrome.Tick += (_, _) => HideChrome();

        // Class-level handler because VideoView hosts our chrome in a second, separate
        // window; a handler on this window alone would miss keys aimed at that one.
        EventManager.RegisterClassHandler(
            typeof(Window), PreviewKeyDownEvent, new KeyEventHandler(OnAnyPreviewKeyDown));

        WireUpInput();

        // XAML text re-reads itself through its bindings; this is for the labels built
        // in code, which have no binding to invalidate.
        Loc.Changed += OnLanguageChanged;

        ApplyVolume(_settings.Volume, _settings.Muted, flash: false);
        UpdateTransportEnabled();

        SourceInitialized += (_, _) =>
        {
            LaunchTrace.Mark("SourceInitialized");
            var background = (Color)FindResource("BackgroundColor");
            Native.ApplyDarkChrome(this, background);

            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

            // Runs before the window is displayed, so the very first erase is dark.
            Native.SetClientBackground(hwnd, background, includeChildren: true);

            // ...and keep it invisible until there is a painted frame to show.
            Native.HideUntilPainted(hwnd);

            System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        };

        // VideoView builds its child window during layout, after SourceInitialized, so
        // that one has to be caught separately or it erases white on first show.
        Video.Loaded += (_, _) =>
            Native.SetClientBackground(
                new System.Windows.Interop.WindowInteropHelper(this).Handle,
                (Color)FindResource("BackgroundColor"), includeChildren: true);

        ContentRendered += OnFirstFrameShown;

        // If the first render is somehow never reported, show the window anyway rather
        // than leaving the user with nothing on screen.
        _revealFallback = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _revealFallback.Tick += (_, _) => RevealWindow();
        Loaded += (_, _) => { LaunchTrace.Mark("Loaded"); _revealFallback.Start(); };
    }

    // ==================================================================
    //  Windows integration
    // ==================================================================

    private void OnFirstFrameShown(object? sender, EventArgs e)
    {
        // Once only, whatever the answer.
        ContentRendered -= OnFirstFrameShown;

        LaunchTrace.Mark("ContentRendered");
        RevealWindow();
        LaunchTrace.Mark("window revealed");

        // Deferred to here so VideoView has built its child window and libvlc has a
        // surface to render into. The window is already dark and start-screen-free.
        if (_startupPath is not null) OpenPath(_startupPath);

        // Iris is portable, so the exe can be moved after it was registered. Repair the
        // stored paths silently rather than leaving Explorer pointing at a stale file.
        if (FileAssociations.IsStale)
        {
            try
            {
                FileAssociations.Register();

                // The move also left the defaults pointing at the old file, so take back
                // the types that are still Iris's to take. Anything another app now owns
                // is left where the user put it.
                DefaultApps.ClaimAvailable();
            }
            catch
            {
                // A broken association is not worth interrupting playback over.
            }
        }

        if (_settings.AssociationPromptShown || FileAssociations.IsRegistered) return;

        _settings.AssociationPromptShown = true;
        _settings.Save();
        ShowAssociationDialog();
    }

    /// <summary>
    /// Undoes <see cref="Native.HideUntilPainted"/>. Called once the first frame is
    /// composed, with a timer as a backstop so a window can never be left invisible if
    /// ContentRendered somehow does not arrive.
    /// </summary>
    private void RevealWindow()
    {
        if (_revealed) return;
        _revealed = true;
        _revealFallback?.Stop();
        Native.Reveal(new System.Windows.Interop.WindowInteropHelper(this).Handle);
    }

    /// <summary>
    /// While fullscreen, tells Windows that "maximised" means the whole monitor rather
    /// than the work area, so the taskbar is covered without moving the window by hand.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WmGetMinMaxInfo && _isFullscreen)
        {
            Native.ApplyFullscreenBounds(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Another Iris was launched — from Explorer, "Open with", or bare. There is only
    /// ever one window, so this one takes the file and comes to the front instead.
    /// </summary>
    public void OnSecondInstance(string? path)
    {
        if (WindowState == WindowState.Minimized)
        {
            // Back to whatever it was before it went to the taskbar, not always Normal.
            // Suppressed because this is Iris restoring the window, not the user resizing it.
            _suppressStateTracking = true;
            try
            {
                WindowState = _restoreState == WindowState.Minimized
                    ? WindowState.Normal
                    : _restoreState;
            }
            finally
            {
                _suppressStateTracking = false;
            }
        }

        Activate();
        Native.BringToFront(new System.Windows.Interop.WindowInteropHelper(this).Handle);

        if (path is not null) OpenPath(path);
    }

    private void OnAssociationLinkClick(object sender, RoutedEventArgs e) => ShowAssociationDialog();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => ShowSettingsDialog();

    /// <summary>Text that is assigned in code rather than bound, so it needs re-applying.</summary>
    private void OnLanguageChanged()
    {
        _settings.Language = Loc.Language;
        _settings.Save();

        UpdateFolderNav();

        // The "Play with Iris" verb and the type names in Settings were written into the
        // registry in the old language, so they only follow along if they are rewritten.
        if (FileAssociations.IsRegistered)
        {
            try
            {
                FileAssociations.Register();
            }
            catch
            {
                // A stale menu label is not worth an error dialog mid-playback.
            }
        }
    }

    private void ShowSettingsDialog() => ShowModal(() => new SettingsDialog(this, _settings));

    private void ShowAssociationDialog() => ShowModal(() => new AssociationDialog(this));

    /// <summary>
    /// Runs a modal dialog with the chrome pinned open underneath it, so the controls do
    /// not fade out behind the dialog and leave it floating over a bare picture.
    /// </summary>
    private void ShowModal(Func<Window> create)
    {
        // The dialog is modal, so stop the chrome fading out underneath it.
        _hideChrome.Stop();
        ShowChrome();

        _dialogOpen = true;
        try
        {
            create().ShowDialog();
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    // ==================================================================
    //  Wiring
    // ==================================================================

    private void WireUpInput()
    {
        OverlayRoot.MouseMove += OnOverlayMouseMove;
        OverlayRoot.MouseLeave += OnOverlayMouseLeave;
        OverlayRoot.MouseLeftButtonDown += OnOverlayClick;
        OverlayRoot.MouseWheel += OnOverlayWheel;

        OverlayRoot.Background = EmptyBackdrop;

        // Clicks and hovers on the bars belong to the bars, not to play/pause.
        // Same layered-window rule as OverlayRoot: alpha 0 would be click-through.
        ControlBar.Background = MouseCatcher;
        TopBar.Background = MouseCatcher;
        ControlBar.MouseLeftButtonDown += (_, e) => e.Handled = true;
        TopBar.MouseLeftButtonDown += (_, e) => e.Handled = true;

        Seek.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnSeekDragStarted));
        Seek.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnSeekDragCompleted));
        Seek.ValueChanged += OnSeekValueChanged;
        Seek.MouseMove += OnSeekHover;
        Seek.MouseEnter += (_, _) => AnimateOpacity(SeekPreview, 1, 120);
        Seek.MouseLeave += (_, _) => AnimateOpacity(SeekPreview, 0, 120);

        Volume.ValueChanged += OnVolumeValueChanged;

        WireUpTrim();

        ControlBar.SizeChanged += (_, _) => UpdateFolderNavWidth();

        foreach (var target in new UIElement[] { this, OverlayRoot })
        {
            target.AllowDrop = true;
            target.DragEnter += OnDragEnter;
            target.DragOver += OnDragOver;
            target.DragLeave += OnDragLeave;
            target.Drop += OnDrop;
        }

        StateChanged += (_, _) =>
        {
            if (_isFullscreen || _suppressStateTracking) return;

            // Minimising says nothing about how big the window should be. Recording it
            // would both lose the state to come back to and be saved as a preference.
            if (WindowState == WindowState.Minimized) return;

            _restoreState = WindowState;
            // Any manual maximise/restore makes the current state the user's own choice.
            if (IsLoaded) _userChangedWindowState = true;
        };
        Closing += OnClosing;
    }

    /// <summary>
    /// Sizes and positions the window before it is shown, including maximising it. The
    /// window is invisible at this point (see <see cref="Native.HideUntilPainted"/>), so
    /// it is created at its final size and never resizes in front of the user.
    /// </summary>
    private void RestoreWindowPlacement()
    {
        var virtualBounds = new Rect(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

        // Clamp as well as floor: a settings file left behind by an older build can carry
        // a full-screen-sized "normal" rectangle, which would restore as a window too big
        // to dock or drag.
        var work = SystemParameters.WorkArea;
        var width = Math.Clamp(_settings.WindowWidth, MinWidth, Math.Max(MinWidth, work.Width));
        var height = Math.Clamp(_settings.WindowHeight, MinHeight, Math.Max(MinHeight, work.Height));

        var saved = new Rect(_settings.WindowLeft, _settings.WindowTop, width, height);

        Width = width;
        Height = height;

        // Only honour a saved position if it still lands on a connected display.
        if (!double.IsNaN(_settings.WindowLeft) && !double.IsNaN(_settings.WindowTop)
            && virtualBounds.IntersectsWith(saved))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _settings.WindowLeft;
            Top = _settings.WindowTop;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = work.Left + (work.Width - width) / 2;
            Top = work.Top + (work.Height - height) / 2;
        }

        // A file passed in from Explorer opens maximised; a bare launch restores
        // whatever size the window was left at. Left/Top/Width/Height are set first so
        // RestoreBounds holds a sane normal-state rect to come back to.
        _forcedMaximize = _startupPath is not null && !_settings.Maximized;

        if (_startupPath is not null || _settings.Maximized)
        {
            WindowState = WindowState.Maximized;
        }

        _restoreState = WindowState;
    }

    // ==================================================================
    //  Shutdown
    // ==================================================================

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _tick.Stop();
        _hideChrome.Stop();
        Loc.Changed -= OnLanguageChanged;

        if (_keepAwake) Native.SetKeepAwake(false);

        // Maximising because a file was opened is our doing, not a preference, so it
        // must not overwrite how the user last chose to leave the window.
        if (!_forcedMaximize || _userChangedWindowState)
        {
            // While fullscreen or minimised the live WindowState is not the one to keep;
            // _restoreState holds the last real choice in both cases.
            var state = _isFullscreen || WindowState == WindowState.Minimized
                ? _restoreState
                : WindowState;
            _settings.Maximized = state == WindowState.Maximized;
        }

        // RestoreBounds is the normal-state rectangle, so this stays correct when the
        // window is closed while maximised instead of saving the maximised size.
        var bounds = _isFullscreen ? _restoreBounds : RestoreBounds;
        if (bounds is { Width: > 0, Height: > 0 }
            && !double.IsInfinity(bounds.Width) && !double.IsInfinity(bounds.Height))
        {
            _settings.WindowLeft = bounds.Left;
            _settings.WindowTop = bounds.Top;
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
        }

        _settings.Save();

        // Detach first: tearing down the player while the view still holds it can hang
        // on libvlc's internal threads.
        Video.MediaPlayer = null;
        _engine.ShutDownInBackground();
    }
}
