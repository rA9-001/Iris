using System.IO;
using System.Windows;
using System.Windows.Input;

namespace Iris;

/// <summary>
/// Keyboard shortcuts and drag-and-drop — the two ways into the player that do not go
/// through a control.
/// </summary>
public partial class MainWindow
{
    // ==================================================================
    //  Keyboard
    // ==================================================================

    private void OnAnyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Not gated on IsActive: VideoView's overlay is a second window of ours and can
        // hold activation, which would silently kill every shortcut. The class handler
        // only ever sees our own windows, so the only thing to exclude is a modal dialog.
        if (_dialogOpen) return;

        // While a clip is being written the only useful key is the one that stops it.
        if (_exporting)
        {
            if (e.Key == Key.Escape) OnExportCancelClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        switch (e.Key)
        {
            case Key.Space or Key.K or Key.MediaPlayPause:
                TogglePlayPause();
                break;

            // The usual shortcut for preferences, and the one the tooltip advertises.
            case Key.OemComma when ctrl:
                ShowSettingsDialog();
                break;

            case Key.Left when ctrl:
                StepFolder(-1);
                break;
            case Key.Right when ctrl:
                StepFolder(1);
                break;

            case Key.Left:
                Nudge(shift ? -1_000 : -SeekSmallStep);
                break;
            case Key.Right:
                Nudge(shift ? 1_000 : SeekSmallStep);
                break;
            case Key.J:
                Nudge(-SeekLargeStep);
                break;
            case Key.L:
                Nudge(SeekLargeStep);
                break;

            // Ctrl with the same keys that do volume: both are "more" and "less" of
            // something about the playback rather than the picture.
            case Key.Up when ctrl:
                StepRate(1);
                break;
            case Key.Down when ctrl:
                StepRate(-1);
                break;

            case Key.Up:
                StepVolume(VolumeStep);
                break;
            case Key.Down:
                StepVolume(-VolumeStep);
                break;
            case Key.M:
                ToggleMute();
                break;

            case Key.F or Key.Enter:
                SetFullscreen(!_isFullscreen);
                break;
            case Key.Escape:
                // Fullscreen first: it is the bigger change to be undone, and leaving
                // trim mode while still fullscreen would strand the window.
                if (_isFullscreen) SetFullscreen(false);
                else if (_trimMode) SetTrimMode(false);
                else return;
                break;

            case Key.T:
                SetTrimMode(!_trimMode);
                break;

            case Key.R:
                CycleEndAction();
                break;

            // "." steps a frame on, as a browser player does. There is no "," to match:
            // libvlc has no reverse step, and see StepFrame for why a seek is not one.
            case Key.OemPeriod:
                StepFrame();
                break;

            case Key.S:
                CopyFrame();
                break;

            // I and O are the editor convention for in and out points. O only opens a
            // file when there is no range being edited, which is the only time it could
            // mean anything else.
            case Key.I when _trimMode:
                SetTrimEdgeHere(start: true);
                break;
            case Key.O when _trimMode:
                SetTrimEdgeHere(start: false);
                break;

            case Key.O:
                BrowseForFile();
                break;

            case Key.Home:
                SeekToFraction(0);
                break;
            case Key.End:
                SeekToFraction(0.999);
                break;

            // Back to normal speed, alongside Ctrl with the arrows.
            case Key.D0 or Key.NumPad0 when ctrl:
                ApplyRate(1.0, flash: true);
                break;

            // Number row jumps to that tenth of the file.
            case >= Key.D0 and <= Key.D9 when !ctrl:
                SeekToFraction((e.Key - Key.D0) / 10.0);
                break;
            case >= Key.NumPad0 and <= Key.NumPad9:
                SeekToFraction((e.Key - Key.NumPad0) / 10.0);
                break;

            default:
                return;
        }

        ShowChrome();
        e.Handled = true;
    }

    // ==================================================================
    //  Drag and drop
    // ==================================================================

    private static string? FirstAcceptedFile(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return null;

        return paths.FirstOrDefault(p => File.Exists(p) && MediaTypes.IsSupported(p));
    }

    private void OnDragEnter(object sender, DragEventArgs e) => OnDragOver(sender, e);

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var accepted = FirstAcceptedFile(e) is not null;
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        if (accepted) AnimateOpacity(DropHighlight, 1, 120);
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => AnimateOpacity(DropHighlight, 0, 120);

    private void OnDrop(object sender, DragEventArgs e)
    {
        AnimateOpacity(DropHighlight, 0, 120);
        var path = FirstAcceptedFile(e);
        if (path is not null) OpenPath(path);
        e.Handled = true;
    }
}
