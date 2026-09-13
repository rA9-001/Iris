using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Iris.Interop;

namespace Iris;

/// <summary>
/// Everything about what the player looks like rather than what it plays: fullscreen,
/// the controls fading themselves out, mouse gestures over the picture, and the centre
/// OSD flash.
/// </summary>
public partial class MainWindow
{
    // ==================================================================
    //  Fullscreen
    // ==================================================================

    private void OnFullscreenClick(object sender, RoutedEventArgs e) => SetFullscreen(!_isFullscreen);

    private void SetFullscreen(bool on)
    {
        if (on == _isFullscreen) return;

        // Iris drives WindowState through both transitions; without this the
        // StateChanged handler would record them as the user's own choice.
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;

        _suppressStateTracking = true;
        try
        {
            if (on)
            {
                _restoreStyle = WindowStyle;
                _restoreResize = ResizeMode;

                // Captured before anything is touched. RestoreBounds, not Left/Top/
                // Width/Height: while maximised those report the maximised rectangle,
                // which would come back as an oversized, undocked normal window.
                _restoreState = WindowState;
                _restoreBounds = WindowState == WindowState.Maximized
                    ? RestoreBounds
                    : new Rect(Left, Top, Width, Height);
                _maximizedRect = WindowState == WindowState.Maximized
                    ? Native.GetWindowRectPx(handle)
                    : default;

                // Set first: WndProc consults it when Windows asks how big "maximised"
                // is, and that question is asked during the style change below.
                _isFullscreen = true;

                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;

                // Fullscreen is just "maximised, with the monitor as the limit". Going
                // through Normal to resize by hand would uncover the screen mid-way and
                // read as a flicker; from a maximised window nothing moves at all.
                if (WindowState != WindowState.Maximized)
                {
                    // Normal -> maximised, and WM_GETMINMAXINFO now reports the whole
                    // monitor, so this single step lands directly on fullscreen.
                    WindowState = WindowState.Maximized;
                }
                else
                {
                    // Already maximised. Windows will not resize a maximised window in
                    // place, so set the rectangle outright; WS_MAXIMIZE stays set.
                    Native.SetWindowRectPx(handle, Native.GetMonitorRectPx(handle));
                }

                Topmost = true;

                TopBar.Visibility = Visibility.Visible;
            }
            else
            {
                // Cleared first for the same reason: the frame change below asks how big
                // "maximised" is, and the answer must now be the work area again.
                _isFullscreen = false;

                Topmost = false;
                WindowStyle = _restoreStyle;
                ResizeMode = _restoreResize;

                if (_restoreState == WindowState.Maximized)
                {
                    // Straight back to the rectangle captured on the way in. Never touch
                    // Left/Top/Width/Height here — on a maximised window that would
                    // overwrite the rectangle it restores down to.
                    Native.SetWindowRectPx(handle, _maximizedRect);
                }
                else
                {
                    WindowState = WindowState.Normal;
                    Left = _restoreBounds.Left;
                    Top = _restoreBounds.Top;
                    Width = _restoreBounds.Width;
                    Height = _restoreBounds.Height;
                }

                TopBar.Visibility = Visibility.Collapsed;
            }
        }
        finally
        {
            _suppressStateTracking = false;
        }

        FullscreenIcon.Data = (Geometry)FindResource(on ? "ExitFullGeometry" : "EnterFullGeometry");
        ShowChrome();
    }

    // ==================================================================
    //  Chrome auto-hide
    // ==================================================================

    private void OnOverlayMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(OverlayRoot);

        // Sub-pixel jitter should not wake the chrome back up.
        if (Math.Abs(pos.X - _lastMousePos.X) < 2 && Math.Abs(pos.Y - _lastMousePos.Y) < 2) return;
        _lastMousePos = pos;

        ShowChrome();
    }

    private void ShowChrome()
    {
        _hideChrome.Stop();

        if (!_chromeVisible)
        {
            _chromeVisible = true;
            AnimateOpacity(Chrome, 1, ChromeFadeInMs);
        }

        SetCursorHidden(false);

        // Nothing to hide behind if there is no video, and never hide out from under the
        // pointer or while a range is being edited.
        if (_mediaLoaded && !_trimMode && !_exporting
            && !ControlBar.IsMouseOver && !TopBar.IsMouseOver)
        {
            _hideChrome.Start();
        }
    }

    private void HideChrome(int fadeMs = ChromeFadeOutMs)
    {
        _hideChrome.Stop();
        if (!_mediaLoaded || ControlBar.IsMouseOver || TopBar.IsMouseOver) return;

        // Trimming is aiming work: the handles have to stay put and stay visible.
        if (_trimMode || _exporting) return;

        _chromeVisible = false;
        AnimateOpacity(Chrome, 0, fadeMs);
        SetCursorHidden(true);
    }

    /// <summary>
    /// The pointer left the video area entirely, so there is nothing to keep the controls
    /// up for. The overlay is its own window, so this also fires when the pointer moves
    /// to the title bar or to another application.
    /// </summary>
    private void OnOverlayMouseLeave(object sender, MouseEventArgs e)
    {
        // A slider drag captures the mouse and can legitimately travel outside the
        // window; yanking the controls away mid-scrub would be worse than leaving them.
        if (_draggingSeek || Mouse.Captured is not null) return;

        HideChrome(ChromeFadeAwayMs);
    }

    private void SetCursorHidden(bool hidden)
    {
        var cursor = hidden ? Cursors.None : Cursors.Arrow;
        Cursor = cursor;
        OverlayRoot.Cursor = cursor;
    }

    // ==================================================================
    //  Mouse
    // ==================================================================

    private void OnOverlayClick(object sender, MouseButtonEventArgs e)
    {
        if (_exporting) { e.Handled = true; return; }

        if (e.ClickCount == 2)
        {
            // The first click of the pair already toggled playback; undo it. With no
            // media that first click opened the file browser instead, and repeating it
            // here would ask for a file twice.
            if (_mediaLoaded) TogglePlayPause();
            SetFullscreen(!_isFullscreen);
        }
        else
        {
            TogglePlayPause();
        }
        e.Handled = true;
    }

    private void OnOverlayWheel(object sender, MouseWheelEventArgs e)
    {
        StepVolume(e.Delta > 0 ? VolumeStep : -VolumeStep);
        e.Handled = true;
    }

    // ==================================================================
    //  OSD
    // ==================================================================

    private void FlashOsdIcon(string geometryKey)
    {
        OsdIcon.Data = (Geometry)FindResource(geometryKey);
        OsdIcon.Visibility = Visibility.Visible;
        OsdText.Visibility = Visibility.Collapsed;
        PlayOsd();
    }

    private void FlashOsdText(string text)
    {
        OsdText.Text = text;
        OsdText.Visibility = Visibility.Visible;
        OsdIcon.Visibility = Visibility.Collapsed;
        PlayOsd();
    }

    private void PlayOsd()
    {
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(450))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(750))));
        Osd.BeginAnimation(OpacityProperty, fade);

        var grow = new DoubleAnimation(0.82, 1.0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        OsdScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        OsdScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    private static void AnimateOpacity(UIElement element, double to, int milliseconds)
    {
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private static string FormatTime(double milliseconds)
    {
        if (double.IsNaN(milliseconds) || milliseconds < 0) milliseconds = 0;
        var t = TimeSpan.FromMilliseconds(milliseconds);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }
}
