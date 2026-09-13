using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using Iris.Localization;

namespace Iris;

/// <summary>
/// Trim mode: pick a range on the timeline and write it out as its own file.
///
/// The timeline is reused rather than duplicated — while trimming, the seek slider is
/// swapped for a range control in the same slot, so there is only ever one timeline on
/// screen to reason about.
/// </summary>
public partial class MainWindow
{
    private bool _trimMode;
    private double _trimStartMs;
    private double _trimEndMs;
    private bool _draggingTrim;

    /// <summary>Opening a 2-minute clip to grab ten seconds is the point of the feature,
    /// so the default selection is a short window around wherever you already are rather
    /// than the whole file, which would mean dragging both handles a long way.</summary>
    private static readonly TimeSpan DefaultSelection = TimeSpan.FromSeconds(10);

    /// <summary>Below this the whole video is short enough to select outright.</summary>
    private static readonly TimeSpan SelectWholeBelow = TimeSpan.FromSeconds(25);

    private CancellationTokenSource? _exportCancel;
    private bool _exporting;

    // ==================================================================
    //  Entering and leaving
    // ==================================================================

    private void OnTrimClick(object sender, RoutedEventArgs e) => SetTrimMode(!_trimMode);

    private void OnTrimCancelClick(object sender, RoutedEventArgs e) => SetTrimMode(false);

    private void SetTrimMode(bool on)
    {
        if (on == _trimMode) return;

        if (on)
        {
            if (!_mediaLoaded || _player is null || _player.Length <= 0)
            {
                FlashOsdText(Loc.Get(TextKey.TrimNothingToTrim));
                return;
            }

            if (!TrimExport.IsSupported)
            {
                MessageBox.Show(this,
                    Loc.Get(TextKey.TrimNeedsNewerWindows) + Environment.NewLine +
                    Environment.NewLine +
                    Loc.Get(TextKey.TrimWorksElsewhere),
                    "Iris", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var length = (double)_player.Length;
            var here = Math.Clamp(_player.Time, 0, length);

            if (length <= SelectWholeBelow.TotalMilliseconds)
            {
                _trimStartMs = 0;
                _trimEndMs = length;
            }
            else
            {
                // Centred on the playhead, then nudged inside the file if it overhangs.
                var half = DefaultSelection.TotalMilliseconds / 2;
                _trimStartMs = here - half;
                _trimEndMs = here + half;

                if (_trimStartMs < 0) { _trimEndMs -= _trimStartMs; _trimStartMs = 0; }
                if (_trimEndMs > length) { _trimStartMs -= _trimEndMs - length; _trimEndMs = length; }
                _trimStartMs = Math.Max(0, _trimStartMs);
            }
        }

        _trimMode = on;

        Seek.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        SeekPreview.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        TrimCanvas.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        TrimHeader.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        TrimIcon.Stroke = (System.Windows.Media.Brush)FindResource(
            on ? "AccentBrush" : "TextPrimaryBrush");

        // The header adds a row above the timeline, so the scrim has to cover more of the
        // picture and darken sooner, or the header text sits on bare video.
        BottomScrim.Height = on ? 196 : 130;
        BottomScrim.Background = (System.Windows.Media.Brush)FindResource(
            on ? "TrimScrimBrush" : "BottomScrimBrush");

        if (on)
        {
            LayoutTrim();
            SeekTo(_trimStartMs);
        }

        // Switching files would throw the range away, so the folder arrows go quiet.
        UpdateFolderNav();

        // The controls must not fade out from under a trim session.
        ShowChrome();
    }

    // ==================================================================
    //  The range control
    // ==================================================================

    private void WireUpTrim()
    {
        TrimStartHandle.DragDelta += (_, e) => DragHandle(start: true, e.HorizontalChange);
        TrimEndHandle.DragDelta += (_, e) => DragHandle(start: false, e.HorizontalChange);

        TrimStartHandle.DragStarted += (_, _) => _draggingTrim = true;
        TrimEndHandle.DragStarted += (_, _) => _draggingTrim = true;
        TrimStartHandle.DragCompleted += (_, _) => _draggingTrim = false;
        TrimEndHandle.DragCompleted += (_, _) => _draggingTrim = false;

        // Clicking the track moves the playhead without disturbing the handles.
        TrimCanvas.MouseLeftButtonDown += OnTrimTrackClick;
        TrimCanvas.SizeChanged += (_, _) => LayoutTrim();
    }

    private double TrimWidth => Math.Max(1, TrimCanvas.ActualWidth);

    private double TimeToX(double ms)
    {
        var length = _player?.Length ?? 0;
        if (length <= 0) return 0;
        return Math.Clamp(ms / length, 0, 1) * TrimWidth;
    }

    private double XToTime(double x)
    {
        var length = _player?.Length ?? 0;
        if (length <= 0) return 0;
        return Math.Clamp(x / TrimWidth, 0, 1) * length;
    }

    private void DragHandle(bool start, double deltaX)
    {
        if (_player is null || _player.Length <= 0) return;

        var length = (double)_player.Length;
        var minimum = TrimExport.MinimumLength.TotalMilliseconds;
        var deltaMs = deltaX / TrimWidth * length;

        if (start)
        {
            _trimStartMs = Math.Clamp(_trimStartMs + deltaMs, 0, _trimEndMs - minimum);
        }
        else
        {
            _trimEndMs = Math.Clamp(_trimEndMs + deltaMs, _trimStartMs + minimum, length);
        }

        // Show the frame under the handle being dragged: picking a cut blind is guesswork.
        SeekTo(start ? _trimStartMs : _trimEndMs);
        LayoutTrim();
    }

    private void OnTrimTrackClick(object sender, MouseButtonEventArgs e)
    {
        // Let the handles have their own clicks.
        if (e.OriginalSource is DependencyObject d && IsInsideHandle(d)) return;

        SeekTo(XToTime(e.GetPosition(TrimCanvas).X));
        LayoutTrim();
        e.Handled = true;
    }

    private static bool IsInsideHandle(DependencyObject node)
    {
        for (var n = node; n is not null; n = System.Windows.Media.VisualTreeHelper.GetParent(n))
        {
            if (n is Thumb) return true;
        }
        return false;
    }

    /// <summary>Positions every piece of the range control. Cheap enough to call freely.</summary>
    private void LayoutTrim()
    {
        if (!_trimMode || _player is null || _player.Length <= 0) return;

        var width = TrimWidth;
        var x0 = TimeToX(_trimStartMs);
        var x1 = TimeToX(_trimEndMs);
        var trackTop = (TrimCanvas.Height - 10) / 2;

        TrimTrack.Width = width;
        Canvas.SetLeft(TrimTrack, 0);
        Canvas.SetTop(TrimTrack, trackTop);

        TrimDimLeft.Width = Math.Max(0, x0);
        Canvas.SetLeft(TrimDimLeft, 0);
        Canvas.SetTop(TrimDimLeft, trackTop);

        TrimDimRight.Width = Math.Max(0, width - x1);
        Canvas.SetLeft(TrimDimRight, x1);
        Canvas.SetTop(TrimDimRight, trackTop);

        TrimSelection.Width = Math.Max(0, x1 - x0);
        Canvas.SetLeft(TrimSelection, x0);
        Canvas.SetTop(TrimSelection, trackTop);

        var playX = TimeToX(_player.Time);
        Canvas.SetLeft(TrimPlayhead, playX - TrimPlayhead.Width / 2);
        Canvas.SetTop(TrimPlayhead, (TrimCanvas.Height - TrimPlayhead.Height) / 2);

        var handleTop = (TrimCanvas.Height - TrimStartHandle.Height) / 2;
        Canvas.SetLeft(TrimStartHandle, x0 - TrimStartHandle.Width / 2);
        Canvas.SetTop(TrimStartHandle, handleTop);
        Canvas.SetLeft(TrimEndHandle, x1 - TrimEndHandle.Width / 2);
        Canvas.SetTop(TrimEndHandle, handleTop);

        var selected = TimeSpan.FromMilliseconds(_trimEndMs - _trimStartMs);
        TrimRangeLabel.Text = Loc.Get(TextKey.TrimRangeLabel,
            FormatTime(_trimStartMs), FormatTime(_trimEndMs), selected.TotalSeconds);
    }

    /// <summary>Sets one edge of the range to wherever the playhead is.</summary>
    private void SetTrimEdgeHere(bool start)
    {
        if (!_trimMode || _player is null || _player.Length <= 0) return;

        var here = (double)_player.Time;
        var minimum = TrimExport.MinimumLength.TotalMilliseconds;

        if (start)
        {
            if (here > _trimEndMs - minimum) { FlashOsdText(Loc.Get(TextKey.TrimTooCloseToEnd)); return; }
            _trimStartMs = Math.Max(0, here);
        }
        else
        {
            if (here < _trimStartMs + minimum) { FlashOsdText(Loc.Get(TextKey.TrimTooCloseToStart)); return; }
            _trimEndMs = Math.Min(_player.Length, here);
        }

        LayoutTrim();
        FlashOsdText(Loc.Get(start ? TextKey.TrimStartSet : TextKey.TrimEndSet));
    }

    // ==================================================================
    //  Saving
    // ==================================================================

    private async void OnTrimSaveClick(object sender, RoutedEventArgs e)
    {
        if (_exporting || !_trimMode || _currentPath is null || _player is null) return;

        var dialog = new SaveFileDialog
        {
            Title = Loc.Get(TextKey.TrimSaveDialogTitle),
            FileName = TrimExport.SuggestName(_currentPath),
            Filter = Loc.Get(TextKey.TrimMp4Filter),
            AddExtension = true,
            DefaultExt = ".mp4",
            OverwritePrompt = true,
        };

        var folder = Path.GetDirectoryName(_currentPath);
        if (Directory.Exists(folder)) dialog.InitialDirectory = folder;

        // The file dialog steals activation; without this the chrome fades underneath it.
        _dialogOpen = true;
        bool? picked;
        try
        {
            picked = dialog.ShowDialog(this);
        }
        finally
        {
            _dialogOpen = false;
        }

        if (picked != true) return;

        await ExportAsync(_currentPath, dialog.FileName,
            TimeSpan.FromMilliseconds(_trimStartMs), TimeSpan.FromMilliseconds(_trimEndMs));
    }

    private async Task ExportAsync(string source, string destination, TimeSpan start, TimeSpan end)
    {
        _exporting = true;
        _exportCancel = new CancellationTokenSource();

        // Rendering and decoding at once just makes both slower.
        var wasPlaying = _player?.IsPlaying == true;
        if (wasPlaying) _player?.Pause();

        _hideChrome.Stop();
        ExportTitle.Text = Loc.Get(TextKey.ExportSaving);
        ExportDetail.Text = Loc.Get(TextKey.ExportDetail,
            (end - start).TotalSeconds, Path.GetFileName(destination));
        ExportProgressFill.Width = 0;
        ExportCancelButton.Visibility = Visibility.Visible;
        ExportOverlay.Visibility = Visibility.Visible;
        UpdateFolderNav();

        // The bar sits in a fixed 380px column, so the fill is a plain fraction of it.
        const double barWidth = 380;
        var progress = new Progress<double>(p =>
            ExportProgressFill.Width = barWidth * Math.Clamp(p / 100.0, 0, 1));

        try
        {
            await TrimExport.ExportAsync(source, destination, start, end, progress, _exportCancel.Token);

            ExportProgressFill.Width = barWidth;
            ExportTitle.Text = Loc.Get(TextKey.ExportSaved);
            ExportDetail.Text = Path.GetFileName(destination);
            ExportCancelButton.Visibility = Visibility.Collapsed;
            await Task.Delay(900);

            SetTrimMode(false);
        }
        catch (OperationCanceledException)
        {
            ExportTitle.Text = Loc.Get(TextKey.ExportCancelled);
            ExportDetail.Text = "";
            await Task.Delay(600);
        }
        catch (Exception ex)
        {
            ExportOverlay.Visibility = Visibility.Collapsed;
            MessageBox.Show(this,
                Loc.Get(TextKey.ExportFailed) + Environment.NewLine + Environment.NewLine + ex.Message,
                "Iris", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            ExportOverlay.Visibility = Visibility.Collapsed;
            _exporting = false;
            _exportCancel?.Dispose();
            _exportCancel = null;

            if (wasPlaying) _player?.Play();
            UpdateFolderNav();
            ShowChrome();
        }
    }

    private void OnExportCancelClick(object sender, RoutedEventArgs e) => _exportCancel?.Cancel();
}
