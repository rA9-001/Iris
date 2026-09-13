using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Iris.Localization;

namespace Iris.Interop;

/// <summary>
/// Registers Iris with Windows as a handler for media files.
///
/// Everything here writes under HKEY_CURRENT_USER, so it needs no installer and no
/// elevation, and <see cref="Unregister"/> removes every trace.
///
/// This puts Iris in the "Open with" menu and in Settings; it does not by itself decide
/// which app opens a file. Choosing the default is <see cref="DefaultApps"/>, which takes
/// only the types Windows leaves available and never forges the hash-protected UserChoice
/// key that records a real decision.
/// </summary>
internal static class FileAssociations
{
    public const string AppName = "Iris";
    private const string ContextVerbKey = "Iris.Play";
    // Shown by Explorer and Settings, so both follow Iris's language.
    private static string AppDescription => Loc.Get(TextKey.AppDescription);
    private static string ContextVerbLabel => Loc.Get(TextKey.ContextVerbPlay);

    private const string SelfKey = @"Software\Iris";
    private const string CapabilitiesKey = @"Software\Iris\Capabilities";
    private const string RegisteredAppsKey = @"Software\RegisteredApplications";
    private const string ClassesKey = @"Software\Classes";

    private static string ProgId(string extension) => "Iris" + extension;   // "Iris.mp4"

    public static string CurrentExePath =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;

    // ==================================================================
    //  State
    // ==================================================================

    /// <summary>The exe path Iris last registered itself from, or null if never registered.</summary>
    public static string? RegisteredExePath
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(SelfKey);
                return key?.GetValue("ExePath") as string;
            }
            catch
            {
                return null;
            }
        }
    }

    public static bool IsRegistered => !string.IsNullOrEmpty(RegisteredExePath);

    /// <summary>
    /// True when Iris is registered but the exe has since been moved or renamed, which
    /// would leave Explorer pointing at a path that no longer exists.
    /// </summary>
    public static bool IsStale
    {
        get
        {
            var registered = RegisteredExePath;
            return !string.IsNullOrEmpty(registered)
                && !string.Equals(registered, CurrentExePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ==================================================================
    //  Register
    // ==================================================================

    public static void Register()
    {
        var exe = CurrentExePath;
        var exeName = Path.GetFileName(exe);
        var command = $"\"{exe}\" \"%1\"";
        var icon = $"\"{exe}\",0";

        // A previous registration may have used a different exe name; clear it first so
        // renaming the file does not leave an orphaned Applications\<old>.exe entry.
        if (IsStale) RemoveKeys(RegisteredExePath!);

        using var classes = Registry.CurrentUser.CreateSubKey(ClassesKey);

        foreach (var type in MediaTypes.All)
        {
            var progId = ProgId(type.Extension);

            using (var prog = classes.CreateSubKey(progId))
            {
                prog.SetValue(null, type.FriendlyName);
                prog.SetValue("FriendlyTypeName", type.FriendlyName);

                using (var iconKey = prog.CreateSubKey("DefaultIcon"))
                    iconKey.SetValue(null, icon);

                using (var open = prog.CreateSubKey(@"shell\open"))
                    open.SetValue(null, Loc.Get(TextKey.ContextVerbOpen));

                using (var cmd = prog.CreateSubKey(@"shell\open\command"))
                    cmd.SetValue(null, command);
            }

            // Puts Iris in the "Open with" list without touching the current default.
            using (var ext = classes.CreateSubKey(type.Extension))
            using (var openWith = ext.CreateSubKey("OpenWithProgIds"))
                openWith.SetValue(progId, string.Empty, RegistryValueKind.String);

            // A named verb (not "open"), so this adds a menu entry and never hijacks
            // what happens on double-click.
            using (var verb = classes.CreateSubKey(
                       $@"SystemFileAssociations\{type.Extension}\shell\{ContextVerbKey}"))
            {
                verb.SetValue("MUIVerb", ContextVerbLabel);
                verb.SetValue("Icon", icon);
                // Iris plays one file at a time; without this, selecting twenty files
                // and hitting the verb would open twenty windows.
                verb.SetValue("MultiSelectModel", "Single");

                using var verbCmd = verb.CreateSubKey("command");
                verbCmd.SetValue(null, command);
            }
        }

        // Identifies the exe itself as an app that can open these types.
        using (var app = classes.CreateSubKey($@"Applications\{exeName}"))
        {
            app.SetValue("FriendlyAppName", AppName);

            using (var iconKey = app.CreateSubKey("DefaultIcon"))
                iconKey.SetValue(null, icon);

            using (var cmd = app.CreateSubKey(@"shell\open\command"))
                cmd.SetValue(null, command);

            // Restricts Iris to media files in the "Open with" list.
            using var supported = app.CreateSubKey("SupportedTypes");
            foreach (var type in MediaTypes.All)
                supported.SetValue(type.Extension, string.Empty);
        }

        // Makes Iris appear in Settings > Default apps so the user can pick it there.
        using (var capabilities = Registry.CurrentUser.CreateSubKey(CapabilitiesKey))
        {
            capabilities.SetValue("ApplicationName", AppName);
            capabilities.SetValue("ApplicationDescription", AppDescription);
            capabilities.SetValue("ApplicationIcon", icon);

            using var associations = capabilities.CreateSubKey("FileAssociations");
            foreach (var type in MediaTypes.All)
                associations.SetValue(type.Extension, ProgId(type.Extension));
        }

        using (var registered = Registry.CurrentUser.CreateSubKey(RegisteredAppsKey))
            registered.SetValue(AppName, CapabilitiesKey);

        using (var self = Registry.CurrentUser.CreateSubKey(SelfKey))
            self.SetValue("ExePath", exe);

        NotifyShell();
    }

    // ==================================================================
    //  Unregister
    // ==================================================================

    public static void Unregister()
    {
        // Hand back any defaults first, while the ProgIds they name still exist.
        DefaultApps.Release();

        RemoveKeys(RegisteredExePath ?? CurrentExePath);

        Try(() =>
        {
            using var registered = Registry.CurrentUser.OpenSubKey(RegisteredAppsKey, writable: true);
            registered?.DeleteValue(AppName, throwOnMissingValue: false);
        });

        Try(() => Registry.CurrentUser.DeleteSubKeyTree(SelfKey, throwOnMissingSubKey: false));

        NotifyShell();
    }

    private static void RemoveKeys(string exePath)
    {
        var exeName = Path.GetFileName(exePath);

        using var classes = Registry.CurrentUser.OpenSubKey(ClassesKey, writable: true);
        if (classes is null) return;

        foreach (var type in MediaTypes.All)
        {
            var progId = ProgId(type.Extension);

            Try(() => classes.DeleteSubKeyTree(progId, throwOnMissingSubKey: false));

            // Only our own value comes out; other apps share this key.
            Try(() =>
            {
                using var openWith = classes.OpenSubKey(
                    $@"{type.Extension}\OpenWithProgIds", writable: true);
                openWith?.DeleteValue(progId, throwOnMissingValue: false);
            });

            Try(() => classes.DeleteSubKeyTree(
                $@"SystemFileAssociations\{type.Extension}\shell\{ContextVerbKey}",
                throwOnMissingSubKey: false));
        }

        Try(() => classes.DeleteSubKeyTree($@"Applications\{exeName}", throwOnMissingSubKey: false));
    }

    // ==================================================================
    //  Handing the default choice back to Windows
    // ==================================================================

    /// <summary>
    /// Opens the Windows "Default apps" page for Iris. Windows 11 deep-links straight to
    /// the app's own file-type list; older builds land on the general page, which is why
    /// the plain URI is used as a fallback.
    /// </summary>
    public static void OpenDefaultAppsSettings()
    {
        if (!TryLaunch($"ms-settings:defaultapps?registeredAppUser={AppName}"))
        {
            TryLaunch("ms-settings:defaultapps");
        }
    }

    private static bool TryLaunch(string uri)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return process is not null || true;
        }
        catch
        {
            return false;
        }
    }

    // ==================================================================
    //  Shell notification
    // ==================================================================

    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    /// <summary>Tells Explorer to re-read associations so the change shows up at once.</summary>
    internal static void NotifyShell() =>
        SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Cleaning up a key that is already gone, or that another app now owns,
            // must not abort the rest of the removal.
        }
    }
}
