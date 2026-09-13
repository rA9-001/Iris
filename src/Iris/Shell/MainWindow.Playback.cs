using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using Iris.Interop;

using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using Iris.Localization;

namespace Iris;

/// <summary>
/// Getting a file playing and keeping the transport in step with it: opening, the
/// play/seek/volume controls, and the 10Hz loop that walks the timeline forward.
/// </summary>
public partial class MainWindow
{
    // ==================================================================
    //  Engine start-up
    // ==================================================================

    /// <summary>
    /// Waits for <see cref="PlaybackEngine"/> and hands the player to the view.
    ///
    /// Attaching and setting the volume are the window's business; everything about
    /// building libvlc itself lives in the engine.
    /// </summary>
    private async Task<MediaPlayer?> GetPlayerAsync()
    {
        if (_engine.Player is not null) return _engine.Player;

        MediaPlayer player;
        try
        {
            player = await _engine.GetPlayerAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                Loc.Get(TextKey.ErrorEngineStart) + Environment.NewLine +
                Environment.NewLine + ex.Message,
                "Iris", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        if (Video.MediaPlayer is null)
        {
            Video.MediaPlayer = player;
            ApplyVolume(_settings.Volume, _settings.Muted, flash: false);

            // Fires on a libvlc thread, so it only posts to the dispatcher: touching the
            // player from inside its own callback is how you deadlock it.
            player.EncounteredError += OnPlayerError;
            player.SnapshotTaken += OnSnapshotTaken;

            LaunchTrace.Mark("player attached");
        }

        return player;
    }

    // ==================================================================
    //  Opening media
    // ==================================================================

    /// <summary>Loads and starts a file. Safe to call before the window is shown.</summary>
    public async void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        // Canonical form from here on. Paths reach Iris from the command line, Explorer,
        // drag-and-drop and the file dialog, and they do not all agree on separators: a
        // path with a forward slash in it never matched the backslashed ones that come
        // back from enumerating its own folder, which silently broke the folder arrows.
        try { path = Path.GetFullPath(path); } catch { /* leave it as given */ }

        // A range picked against the previous video means nothing against this one.
        SetTrimMode(false);

        _currentPath = path;
        _endReached = false;
        _startedPlaying = false;
        _deadTicks = 0;
        _steppedMs = null;
        _mediaLoaded = true;
        ClearErrorState();
        ResetTransport();

        // Show the title and clear the start screen straight away; the engine may still
        // be a few milliseconds behind on a cold launch and there is no reason to wait.
        ApplyMediaTitle(path);
        RefreshFolder(path);
        EmptyState.Visibility = Visibility.Collapsed;
        UpdateTransportEnabled();
        ShowChrome();

        LaunchTrace.Mark("OpenPath entry");
        var player = await GetPlayerAsync();
        if (player is null || _libVlc is null) return;

        // Another file may have been opened while the engine was starting.
        if (!string.Equals(_currentPath, path, StringComparison.Ordinal)) return;

        bool started;
        using (var media = new Media(_libVlc, new Uri(path)))
        {
            // Play() reports whether libvlc accepted the media at all. It still returns
            // true for a file that only fails once decoding starts, which is what the
            // Error state in OnTick is for.
            started = player.Play(media);
            LaunchTrace.Mark("Play() returned");
        }

        if (!started)
        {
            EnterErrorState();
            return;
        }


        // Volume and rate are applied from OnTick once playback has actually started;
        // libvlc ignores both at this point.
        _pendingPlaybackSettings = true;

        _settings.LastFolder = Path.GetDirectoryName(path);

        // Do NOT go transparent yet. libvlc has not opened a video output at this point,
        // so the video child window is still unpainted and would show whatever is behind
        // Iris. OnTick swaps the backdrop once VoutCount reports a real output.
        _awaitingVideo = true;
    }

    /// <summary>
    /// Puts the transport back to the start of nothing.
    ///
    /// libvlc reports no time for a newly opened file until it has actually begun, which
    /// for a large one is a few hundred milliseconds and can be much longer. Without this
    /// the bar and the clock keep showing the file that just finished — full, and sitting
    /// at its duration — so the video that is loading looks like it has already ended.
    /// </summary>
    private void ResetTransport()
    {
        _suppressSeek = true;
        Seek.Maximum = 1;          // replaced by the real length on the first tick
        Seek.Value = 0;
        _suppressSeek = false;

        TimeLabel.Text = FormatTime(0);
        DurationLabel.Text = FormatTime(0);
    }

    private void ApplyMediaTitle(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        Title = $"{name} — Iris";
        TopTitle.Text = name;
        CentreTitle.Text = name;
    }

    private void OnOpenClick(object sender, RoutedEventArgs e) => BrowseForFile();

    private void BrowseForFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.Get(TextKey.OpenDialogTitle),
            Filter = MediaTypes.DialogFilter,
            CheckFileExists = true,
        };

        if (Directory.Exists(_settings.LastFolder))
        {
            dialog.InitialDirectory = _settings.LastFolder;
        }

        if (dialog.ShowDialog(this) == true)
        {
            OpenPath(dialog.FileName);
        }
    }

    // ==================================================================
    //  Transport
    // ==================================================================

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => TogglePlayPause();

    private void TogglePlayPause()
    {
        if (!_mediaLoaded)
        {
            BrowseForFile();
            return;
        }

        if (_endReached)
        {
            Replay();
            return;
        }

        if (_player is null) return;

        if (_player.IsPlaying)
        {
            _player.Pause();
            FlashOsdIcon("PauseGeometry");
        }
        else
        {
            _player.Play();
            FlashOsdIcon("PlayGeometry");
        }
    }

    private void Replay()
    {
        if (_currentPath is null || _player is null || _libVlc is null) return;
        _endReached = false;
        using var media = new Media(_libVlc, new Uri(_currentPath));
        _player.Play(media);
        FlashOsdIcon("PlayGeometry");
    }

    private void OnBack10Click(object sender, RoutedEventArgs e) => Nudge(-SeekLargeStep);
    private void OnFwd10Click(object sender, RoutedEventArgs e) => Nudge(SeekLargeStep);
    private void OnBack5Click(object sender, RoutedEventArgs e) => Nudge(-SeekSmallStep);
    private void OnFwd5Click(object sender, RoutedEventArgs e) => Nudge(SeekSmallStep);

    private void Nudge(double deltaMs)
    {
        if (!_mediaLoaded || _player is null || _player.Length <= 0) return;

        // Seeking past the end while stopped leaves libvlc in Ended; clamp just short of it.
        var target = Math.Clamp(_player.Time + deltaMs, 0, Math.Max(0, _player.Length - 250));

        if (_endReached && deltaMs < 0)
        {
            Replay();
            _player.Time = (long)target;
        }
        else
        {
            _player.Time = (long)target;
        }

        _suppressSeek = true;
        Seek.Value = target;
        _suppressSeek = false;
        TimeLabel.Text = FormatTime(target);

        FlashOsdText(Loc.Get(
            deltaMs > 0 ? TextKey.OsdSkipForward : TextKey.OsdSkipBack, deltaMs / 1000));
    }

    private void SeekToFraction(double fraction)
    {
        if (!_mediaLoaded || _player is null || _player.Length <= 0) return;
        var target = Math.Clamp(fraction, 0, 1) * _player.Length;
        if (_endReached) Replay();
        _player.Time = (long)target;
    }

    // ==================================================================
    //  Seek bar
    // ==================================================================

    private void OnSeekDragStarted(object sender, DragStartedEventArgs e) => _draggingSeek = true;

    private void OnSeekDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _draggingSeek = false;
        SeekTo(Seek.Value);
    }

    private void OnSeekValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSeek) return;

        if (_draggingSeek)
        {
            // Scrubbing: keep the readout live but do not hammer the decoder on every pixel.
            TimeLabel.Text = FormatTime(e.NewValue);
            return;
        }

        // Click-to-position on the track.
        SeekTo(e.NewValue);
    }

    /// <summary>
    /// Moves the playhead and keeps the readout with it. Every seek in the app goes
    /// through here — the slider, the skip buttons, the number keys and the trim handles.
    /// </summary>
    private void SeekTo(double ms)
    {
        if (_player is null || _player.Length <= 0) return;

        // Seeking exactly to the end leaves libvlc in Ended; stop just short of it.
        if (_endReached) Replay();
        _steppedMs = null;
        _player.Time = (long)Math.Clamp(ms, 0, Math.Max(0, _player.Length - 250));
        TimeLabel.Text = FormatTime(ms);
    }

    private void OnSeekHover(object sender, MouseEventArgs e)
    {
        if (_player is null || _player.Length <= 0) return;

        var width = Seek.ActualWidth;
        if (width <= 0) return;

        var x = Math.Clamp(e.GetPosition(Seek).X, 0, width);
        SeekPreviewText.Text = FormatTime(x / width * _player.Length);

        // Centre the bubble on the cursor, but keep it inside the bar.
        SeekPreview.UpdateLayout();
        var half = SeekPreview.ActualWidth / 2;
        SeekPreviewOffset.X = Math.Clamp(x - half, 0, Math.Max(0, width - half * 2));
    }

    // ==================================================================
    //  Volume
    // ==================================================================

    private void OnMuteClick(object sender, RoutedEventArgs e) => ToggleMute();

    private void ToggleMute()
    {
        ApplyVolume(_settings.Volume, !_settings.Muted, flash: true);
    }

    private void OnVolumeValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressVolume) return;
        ApplyVolume((int)Math.Round(e.NewValue), muted: false, flash: false);
    }

    private void StepVolume(int delta)
    {
        ApplyVolume(Math.Clamp(_settings.Volume + delta, 0, AppSettings.MaxVolume),
            muted: false, flash: true);
    }

    private void ApplyVolume(int volume, bool muted, bool flash)
    {
        volume = Math.Clamp(volume, 0, AppSettings.MaxVolume);
        _settings.Volume = volume;
        _settings.Muted = muted;

        if (_player is not null)
        {
            _player.Mute = muted;
            _player.Volume = muted ? 0 : volume;

            // Reading it straight back catches a libvlc build that refuses to amplify:
            // the slider would still say 150 while the sound stayed at 100.
            if (LaunchTrace.Tracing)
                LaunchTrace.Mark($"volume {volume} -> libvlc reports {_player.Volume}");
        }

        _suppressVolume = true;
        Volume.Value = volume;
        _suppressVolume = false;

        var waves = muted || volume == 0 ? "MuteGeometry"
                  : volume < 50 ? "WavesLowGeometry"
                  : "WavesHighGeometry";
        WavesIcon.Data = (Geometry)FindResource(waves);
        SpeakerIcon.Opacity = muted ? 0.55 : 1.0;
        WavesIcon.Opacity = muted ? 0.55 : 1.0;

        if (flash) FlashOsdText(muted ? Loc.Get(TextKey.OsdMuted) : $"{volume}%");
    }

    // ==================================================================
    //  End of a file
    // ==================================================================

    private void HandleEndOfFile()
    {
        // Never take over while a clip is being marked up or written: switching files
        // would throw the range away, and looping would fight the export.
        if (_trimMode || _exporting) return;

        switch (_settings.WhenFileEnds)
        {
            case EndAction.Repeat:
                Replay();
                break;

            // Falls through to stopping on the last file in the folder, rather than
            // wrapping round to the first and looking like it never ends.
            case EndAction.PlayNext when Neighbour(1) is not null:
                StepFolder(1);
                break;
        }
    }

    /// <summary>Cycles stop → repeat → play next, and says out loud where it landed.</summary>
    private void CycleEndAction()
    {
        _settings.WhenFileEnds = _settings.WhenFileEnds switch
        {
            EndAction.Stop => EndAction.Repeat,
            EndAction.Repeat => EndAction.PlayNext,
            _ => EndAction.Stop,
        };
        _settings.Save();

        FlashOsdText(Loc.Get(DescribeEndAction(_settings.WhenFileEnds)));

        // Already sitting on the last frame: act on the new choice now, instead of making
        // the user seek back and wait for the end to come round again.
        if (_endReached) HandleEndOfFile();
    }

    internal static TextKey DescribeEndAction(EndAction action) => action switch
    {
        EndAction.Repeat => TextKey.EndRepeat,
        EndAction.PlayNext => TextKey.EndPlayNext,
        _ => TextKey.EndStop,
    };

    // ==================================================================
    //  Speed
    // ==================================================================

    // The button only moves one way, so it wraps round; the shortcuts stop at the ends,
    // where wrapping from 2× straight back to 0.5× would be a nasty surprise.
    private void OnSpeedClick(object sender, RoutedEventArgs e) => StepRate(1, wrap: true);

    private void StepRate(int delta, bool wrap = false)
    {
        var index = Array.IndexOf(Rates, _rate);
        if (index < 0) index = Array.IndexOf(Rates, 1.0);

        index += delta;
        index = wrap
            ? (index % Rates.Length + Rates.Length) % Rates.Length
            : Math.Clamp(index, 0, Rates.Length - 1);

        ApplyRate(Rates[index], flash: true);
    }

    private void ApplyRate(double rate, bool flash)
    {
        _rate = rate;

        // Ignore what SetRate returns: libvlc's C call reports success as 0 and the
        // binding reads that as false, so it says "failed" every time it works. The
        // rate read back is the only meaningful check.
        _player?.SetRate((float)rate);

        if (LaunchTrace.Tracing)
            LaunchTrace.Mark($"rate {rate} -> libvlc reports {_player?.Rate}");

        SpeedLabel.Text = Loc.Get(TextKey.SpeedLabel, rate);

        // Normal speed stays quiet; anything else is worth noticing, because it is the
        // explanation for why the picture looks wrong.
        SpeedLabel.Foreground = (Brush)FindResource(
            rate == 1.0 ? "TextTertiaryBrush" : "AccentBrush");

        if (flash) FlashOsdText(Loc.Get(TextKey.SpeedLabel, rate));
    }

    // ==================================================================
    //  Frame stepping
    // ==================================================================

    /// <summary>
    /// One frame on, paused — "." as a browser player does it.
    ///
    /// Forwards only. libvlc has no reverse step, and a seek is not a stand-in for one:
    /// a seek moves the position but does not reliably repaint the video, so stepping
    /// built on seeks registered roughly one press in four while the clock advanced every
    /// time. NextFrame decodes and displays by definition, which is what makes this
    /// direction exact and immediate — and there is no equivalent going the other way.
    /// </summary>
    private void StepFrame()
    {
        if (!CanStep() || _endReached) return;

        // SetPause is explicit where Pause() is a toggle, and a toggle arriving while
        // already paused would start the video playing again mid-step.
        _player!.SetPause(true);
        _player.NextFrame();

        // libvlc does not move its reported time when it steps, so the position is
        // carried here, to the exact frame rather than the whole millisecond: at 29.98fps
        // a frame is 33.35ms, and rounding every step would eventually skip one.
        _steppedMs ??= _player.Time;
        ShowSteppedPosition(_steppedMs.Value + FrameMs());
    }

    private bool CanStep() =>
        _mediaLoaded && !_playbackFailed && _player is not null && _player.Length > 0;

    private void ShowSteppedPosition(double ms)
    {
        if (_player is null) return;

        _steppedMs = Math.Clamp(ms, 0, _player.Length);

        _suppressSeek = true;
        Seek.Value = Math.Min(_steppedMs.Value, Seek.Maximum);
        _suppressSeek = false;
        TimeLabel.Text = FormatTime(_steppedMs.Value);

        ShowChrome();
    }

    /// <summary>One frame in milliseconds, or a sane figure when the file will not say.</summary>
    private double FrameMs()
    {
        var fps = _player?.Fps ?? 0f;
        return fps > 1f ? 1000.0 / fps : 40.0;
    }

    // ==================================================================
    //  Copying a frame
    // ==================================================================

    private void CopyFrame()
    {
        if (!_mediaLoaded || _playbackFailed || _player is null) return;

        // libvlc can only write a snapshot to a file, and writes it on one of its own
        // threads, so the result is collected from SnapshotTaken rather than assumed to
        // be on disk by the time this returns. 0, 0 keeps the video's own size.
        var path = Path.Combine(Path.GetTempPath(), $"iris-frame-{Guid.NewGuid():N}.png");

        if (!_player.TakeSnapshot(0, path, 0, 0))
            FlashOsdText(Loc.Get(TextKey.OsdSnapshotFailed));
    }

    private void OnSnapshotTaken(object? sender, MediaPlayerSnapshotTakenEventArgs e)
    {
        // Raised on a libvlc thread; the clipboard and the OSD are the UI thread's.
        var path = e.Filename;
        Dispatcher.BeginInvoke(() => CopyFrameToClipboard(path));
    }

    private void CopyFrameToClipboard(string path)
    {
        try
        {
            var frame = new BitmapImage();
            frame.BeginInit();

            // Reads the file now rather than lazily, which is what lets it be deleted
            // immediately afterwards instead of being held open for the session.
            frame.CacheOption = BitmapCacheOption.OnLoad;
            frame.UriSource = new Uri(path);
            frame.EndInit();
            frame.Freeze();

            PutOnClipboard(frame);
            FlashOsdText(Loc.Get(TextKey.OsdSnapshotCopied));
        }
        catch
        {
            FlashOsdText(Loc.Get(TextKey.OsdSnapshotFailed));
        }
        finally
        {
            try { File.Delete(path); } catch { /* a stray temp file is harmless */ }
        }
    }

    /// <summary>
    /// The clipboard is shared, and any other program can have it open for a moment. That
    /// surfaces as a COM failure rather than a wait, so a first attempt is not conclusive.
    /// </summary>
    private static void PutOnClipboard(BitmapSource frame)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Clipboard.SetImage(frame);
                return;
            }
            catch (COMException) when (attempt < 4)
            {
                Thread.Sleep(60);
            }
        }
    }

    // ==================================================================
    //  A file that will not play
    // ==================================================================

    /// <summary>
    /// Shown instead of a black window when libvlc cannot open or decode the file. Before
    /// this existed the window simply sat there, titled and silent, with a dead transport
    /// and nothing to say what had gone wrong.
    /// </summary>
    private void EnterErrorState()
    {
        _playbackFailed = true;
        _mediaLoaded = false;
        _awaitingVideo = false;
        _endReached = false;

        // No video output was ever opened, so the overlay goes back to being opaque:
        // left transparent it would show whatever is behind Iris.
        OverlayRoot.Background = EmptyBackdrop;

        ErrorFileName.Text = _currentPath is null ? string.Empty : Path.GetFileName(_currentPath);
        EmptyState.Visibility = Visibility.Collapsed;
        ErrorState.Visibility = Visibility.Visible;

        Title = "Iris";
        UpdateTransportEnabled();
        ShowChrome();
    }

    private void OnPlayerError(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (!_playbackFailed) EnterErrorState();
        });

    private void ClearErrorState()
    {
        _playbackFailed = false;
        ErrorState.Visibility = Visibility.Collapsed;
    }

    // ==================================================================
    //  Frame loop
    // ==================================================================

    /// <summary>Consecutive ticks ended with nothing to play; see OnTick.</summary>
    private int _deadTicks;

    private void OnTick(object? sender, EventArgs e)
    {
        if (_player is null) return;

        var playing = _player.IsPlaying;

        if (LaunchTrace.Tracing)
        {
            var state = _player.State;
            if (state != _tracedState)
            {
                _tracedState = state;
                LaunchTrace.Mark($"state -> {state}  (vout={_player.VoutCount})");
            }
        }

        // Audio-only files never open a video output, so the backdrop correctly stays
        // opaque for them and this simply never fires.
        if (_awaitingVideo && _player.VoutCount > 0)
        {
            _awaitingVideo = false;

            // Order matters. libvlc has only just created its video windows, and the
            // pillarbox bars either side of the picture belong to one of them, which
            // still erases white. Darken them while the opaque backdrop is over the top,
            // then uncover: the repaint happens out of sight.
            Native.DarkenVideoSurface(
                new System.Windows.Interop.WindowInteropHelper(this).Handle,
                (Color)FindResource("BackgroundColor"));

            OverlayRoot.Background = MouseCatcher;
            LaunchTrace.Mark("first video frame");
        }

        // Playing again means libvlc's own clock is authoritative once more.
        if (playing) _steppedMs = null;

        if (_pendingPlaybackSettings && playing)
        {
            _pendingPlaybackSettings = false;

            // Order matters only in that both need a live input; volume first because a
            // wrong volume is the more noticeable of the two.
            ApplyVolume(_settings.Volume, _settings.Muted, flash: false);
            ApplyRate(_rate, flash: false);
        }

        PlayIcon.Data = (Geometry)FindResource(playing ? "PauseGeometry" : "PlayGeometry");

        // Polled rather than taken from libvlc's event, which arrives on an internal
        // thread where re-entering the player can deadlock.
        if (_player.State == VLCState.Error && !_playbackFailed)
        {
            EnterErrorState();
            return;
        }

        if (playing) _startedPlaying = true;

        // libvlc does not always raise an error for a file it cannot make sense of. A
        // damaged MP4 is opened happily, yields no duration and no output, and goes
        // straight to Ended without ever reporting Playing — so this cannot wait for the
        // file to have started. Counted over several ticks instead, because for a moment
        // after Play() the previous file's Ended can coincide with the new one not having
        // a length yet, and one tick of that must not raise a false failure.
        if (_player.State == VLCState.Ended && _player.Length <= 0)
        {
            if (++_deadTicks >= 3 && !_playbackFailed)
            {
                EnterErrorState();
                return;
            }
        }
        else
        {
            _deadTicks = 0;
        }

        // A real ending, as opposed to a leftover one. Gated on having actually seen this
        // file play: between Play() and libvlc leaving the previous file's Ended state,
        // the old state would otherwise read as this file finishing before it started.
        // A stale Ended always carries the previous file's length, which is why the
        // failure check above is safe without the same guard.
        if (_player.State == VLCState.Ended && _player.Length > 0
            && _startedPlaying && !_endReached)
        {
            _endReached = true;

            if (LaunchTrace.Tracing)
                LaunchTrace.Mark($"END REACHED for {Path.GetFileName(_currentPath)} (len={_player.Length})");

            _suppressSeek = true;
            Seek.Value = Seek.Maximum;
            _suppressSeek = false;
            TimeLabel.Text = FormatTime(Seek.Maximum);
            ShowChrome();

            HandleEndOfFile();
        }

        var length = _player.Length;
        if (length > 0 && Math.Abs(Seek.Maximum - length) > 1)
        {
            Seek.Maximum = length;
            DurationLabel.Text = FormatTime(length);
            UpdateTransportEnabled();
        }

        if (!_draggingSeek && !_endReached && _steppedMs is null)
        {
            var time = _player.Time;
            if (time >= 0)
            {
                _suppressSeek = true;
                Seek.Value = Math.Min(time, Seek.Maximum);
                _suppressSeek = false;
                TimeLabel.Text = FormatTime(time);
            }
        }

        if (_trimMode)
        {
            // Loop the selection while playing, so what you hear and see is the clip you
            // are about to save rather than whatever follows it.
            if (playing && !_draggingTrim && _player.Time > _trimEndMs)
            {
                SeekTo(_trimStartMs);
            }

            LayoutTrim();
        }

        // Only hold the display awake while frames are actually moving.
        if (playing != _keepAwake)
        {
            _keepAwake = playing;
            Native.SetKeepAwake(playing);
        }
    }

    private void UpdateTransportEnabled()
    {
        var ready = _mediaLoaded;
        TrimButton.IsEnabled = ready;
        Back10Button.IsEnabled = ready;
        Back5Button.IsEnabled = ready;
        Fwd5Button.IsEnabled = ready;
        Fwd10Button.IsEnabled = ready;
        Seek.IsEnabled = ready;
        UpdateFolderNavWidth();
        UpdateFolderNav();
    }
}
