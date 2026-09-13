using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Iris.Localization;

namespace Iris;

/// <summary>
/// Stepping through the folder the open file came from.
///
/// Opening one clip out of a folder of them almost always means the neighbours are
/// interesting too, so Iris lists what is beside the current file and puts the rest of
/// the folder one click away rather than one trip through the file dialog.
/// </summary>
public partial class MainWindow
{
    private string[] _folderFiles = [];
    private int _folderIndex = -1;

    /// <summary>Folder the current listing came from, so stepping between neighbours does
    /// not rescan what we already know.</summary>
    private string? _scannedFolder;

    /// <summary>Below this the left and right clusters would meet in the middle.</summary>
    private const double NavMinimumWidth = 900;

    // ==================================================================
    //  Listing
    // ==================================================================

    private async void RefreshFolder(string path) => await ListFolderAsync(path, rescan: false);

    /// <summary>
    /// Brings the listing in line with <paramref name="path"/> and returns whether the
    /// file was found in it. Scanning happens on a worker: a folder on a slow or network
    /// drive must not hold up playback starting.
    /// </summary>
    private async Task<bool> ListFolderAsync(string path, bool rescan)
    {
        var folder = Path.GetDirectoryName(path);

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            _folderFiles = [];
            _folderIndex = -1;
            _scannedFolder = null;
            UpdateFolderNav();
            return false;
        }

        var known = !rescan
            && string.Equals(_scannedFolder, folder, StringComparison.OrdinalIgnoreCase);

        if (!known)
        {
            string[] found;
            try
            {
                found = await Task.Run(() => Scan(folder));
            }
            catch
            {
                // An unreadable folder is not worth interrupting playback over; the
                // arrows simply do not appear.
                found = [];
            }

            // Another file may have been opened while this was scanning.
            if (!string.Equals(_currentPath, path, StringComparison.Ordinal)) return false;

            _folderFiles = found;
            _scannedFolder = folder;
            LaunchTrace.Mark($"folder listed: {found.Length} media files");
        }

        _folderIndex = IndexOf(_folderFiles, path);
        UpdateFolderNav();
        return _folderIndex >= 0;
    }

    private static string[] Scan(string folder)
    {
        var files = Directory.EnumerateFiles(folder).Where(MediaTypes.IsSupported).ToArray();

        // Explorer's ordering, so "clip2" comes before "clip10" the way it does in the
        // window the file was picked from.
        Array.Sort(files, static (a, b) =>
            StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)));

        return files;
    }

    /// <summary>
    /// Finds a file in the listing. Entries come from enumerating the folder so they are
    /// already canonical; the path being looked up may not be, and comparing the two raw
    /// is what once made the arrows silently never appear.
    /// </summary>
    private static int IndexOf(string[] files, string path)
    {
        string target;
        try { target = Path.GetFullPath(path); }
        catch { target = path; }

        return Array.FindIndex(files, f => string.Equals(f, target, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Explorer's own natural sort: digits compare by value, not by character.</summary>
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string a, string b);

    // ==================================================================
    //  Moving
    // ==================================================================

    private void OnPreviousFileClick(object sender, RoutedEventArgs e) => StepFolder(-1);

    private void OnNextFileClick(object sender, RoutedEventArgs e) => StepFolder(1);

    private async void StepFolder(int delta)
    {
        if (_trimMode || _exporting || _currentPath is null) return;

        var target = Neighbour(delta);

        // Nothing there, or the file has gone since the folder was listed. Rescan once
        // before giving up, so a deletion or a newly dropped file cannot wedge the arrows.
        if (target is null)
        {
            if (!await ListFolderAsync(_currentPath, rescan: true)) return;
            target = Neighbour(delta);
        }

        if (target is not null) OpenPath(target);
    }

    /// <summary>The file <paramref name="delta"/> places away, if there is one on disk.</summary>
    private string? Neighbour(int delta)
    {
        if (_folderIndex < 0) return null;

        var index = _folderIndex + delta;
        if (index < 0 || index >= _folderFiles.Length) return null;

        var candidate = _folderFiles[index];
        return File.Exists(candidate) ? candidate : null;
    }

    // ==================================================================
    //  The control
    // ==================================================================

    private void UpdateFolderNav()
    {
        // One file in the folder means two permanently dead arrows, so show none.
        var many = _folderFiles.Length > 1 && _folderIndex >= 0;
        var visible = many ? Visibility.Visible : Visibility.Collapsed;

        PrevFileButton.Visibility = visible;
        NextFileButton.Visibility = visible;

        if (!many)
        {
            CentreTitle.ToolTip = null;
            return;
        }

        // Switching files throws a trim range away, so the arrows go quiet while one is
        // being set rather than losing it to a stray click.
        var canStep = !_trimMode && !_exporting;

        PrevFileButton.IsEnabled = canStep && _folderIndex > 0;
        NextFileButton.IsEnabled = canStep && _folderIndex < _folderFiles.Length - 1;

        PrevFileButton.ToolTip = Describe(PrevFileButton.IsEnabled, -1, Loc.Get(TextKey.NavPrevious), Loc.Get(TextKey.NavShortcutPrevious));
        NextFileButton.ToolTip = Describe(NextFileButton.IsEnabled, +1, Loc.Get(TextKey.NavNext), Loc.Get(TextKey.NavShortcutNext));

        CentreTitle.ToolTip = Loc.Get(TextKey.NavPosition,
            _folderIndex + 1, _folderFiles.Length, _scannedFolder);
    }

    private string? Describe(bool enabled, int delta, string label, string shortcut) =>
        enabled
            ? $"{label}:  {Path.GetFileName(_folderFiles[_folderIndex + delta])}   ({shortcut})"
            : null;

    /// <summary>The centre has to give way before the two clusters reach it.</summary>
    private void UpdateFolderNavWidth() =>
        FolderNav.Visibility = _mediaLoaded && ControlBar.ActualWidth >= NavMinimumWidth
            ? Visibility.Visible
            : Visibility.Collapsed;
}
