using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Iris.Interop;

/// <summary>
/// Works out which media types Iris is actually opened for, and claims the ones Windows
/// leaves available.
///
/// The rules Windows enforces, and how this stays inside them:
///
/// * The default handler lives in a per-extension <c>UserChoice</c> key protected by an
///   undocumented hash. Nothing here ever writes one — forging it is fragile and hostile,
///   and Windows resets associations it did not sanction.
/// * A <c>UserChoice</c> naming an app that is still installed is a real decision the
///   person made. It is left strictly alone; only Windows' own UI may change it.
/// * A <c>UserChoice</c> naming a ProgId that no longer resolves to an installed program
///   is a dangling pointer, not a decision. Windows reacts to one by dropping the file
///   type on the "How do you want to open this?" picker every single time. Removing the
///   stale key — the same thing Settings' own "Reset" does — is allowed, needs no hash,
///   and is what lets a working handler take over.
/// * With no <c>UserChoice</c> in the way, the per-user class default under
///   <c>HKCU\Software\Classes</c> decides, which any app may write for its own account.
///
/// So Iris can claim a type when it is unclaimed or held by a ghost, and cannot when a
/// real app holds it. <see cref="Survey"/> reports which is which.
/// </summary>
internal static class DefaultApps
{
    private const string FileExtsKey =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";

    public enum Status
    {
        /// <summary>Iris opens this type.</summary>
        Iris,

        /// <summary>Nothing holds it; Iris may take it.</summary>
        Available,

        /// <summary>Held by a choice pointing at software that is no longer installed.</summary>
        Stale,

        /// <summary>Held by an installed app. Only the user can change this, in Settings.</summary>
        Taken,
    }

    public sealed record TypeStatus(MediaTypes.MediaType Type, Status Status, string? Holder)
    {
        /// <summary>True when <see cref="Claim"/> would be allowed to take this type.</summary>
        public bool IsClaimable => Status is Status.Available or Status.Stale;
    }

    // ==================================================================
    //  Survey
    // ==================================================================

    public static IReadOnlyList<TypeStatus> Survey()
    {
        var results = new List<TypeStatus>(MediaTypes.All.Length);

        foreach (var type in MediaTypes.All)
            results.Add(Inspect(type));

        return results;
    }

    private static TypeStatus Inspect(MediaTypes.MediaType type)
    {
        // Ask the shell what a double-click would really launch rather than deducing it
        // from the keys: the resolution order has enough Windows-version-specific corners
        // (UserChoice, UserChoiceLatest, the "open with" MRU, the class default) that
        // reading them back is guesswork, and this is the same answer Explorer uses.
        if (ResolvesToIris(type.Extension))
            return new TypeStatus(type, Status.Iris, FileAssociations.AppName);

        // Not Iris: the question becomes whether anything real is holding the type.
        var choice = UserChoiceProgId(type.Extension);

        if (string.IsNullOrEmpty(choice))
            return new TypeStatus(type, Status.Available, null);

        return IsLive(choice)
            ? new TypeStatus(type, Status.Taken, DescribeProgId(choice))
            : new TypeStatus(type, Status.Stale, DescribeProgId(choice));
    }

    private static bool ResolvesToIris(string extension)
    {
        var handler = ResolvedExecutable(extension);

        return !string.IsNullOrEmpty(handler)
            && string.Equals(handler, FileAssociations.CurrentExePath,
                   StringComparison.OrdinalIgnoreCase);
    }

    // ==================================================================
    //  Claim
    // ==================================================================

    /// <summary>
    /// Makes Iris the default for every type Windows allows, leaving the rest untouched.
    /// Returns how many types Iris now opens and how many are still held by another app.
    /// </summary>
    public static (int Claimed, int Blocked) ClaimAvailable()
    {
        var changed = false;

        foreach (var status in Survey())
        {
            if (status.IsClaimable && Claim(status)) changed = true;
        }

        if (changed) FileAssociations.NotifyShell();

        // Report what is true afterwards, not what was attempted.
        var after = Survey();

        return (after.Count(s => s.Status == Status.Iris),
                after.Count(s => s.Status != Status.Iris));
    }

    private static bool Claim(TypeStatus status)
    {
        if (!status.IsClaimable) return false;

        var extension = status.Type.Extension;

        try
        {
            // Clear pointers to software that is gone. Without this Windows keeps
            // showing the picker no matter what the class default says.
            if (status.Status == Status.Stale && !DeleteUserChoice(extension))
                return false;

            // Windows 11 also keeps a hash-only record of the last choice made for the
            // type, and treats its presence as "this one has been decided" — which sends
            // the file to the picker when the decision no longer resolves. It carries no
            // ProgId, so it is only ever cleared for a type already being claimed, never
            // for one an installed app still holds.
            DeleteChoiceRecord(extension, "UserChoiceLatest");

            // Shadows the machine-wide default for this account only.
            using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}"))
                key.SetValue(null, IrisProgId(extension));

            PromoteInOpenWithList(extension);

            return true;
        }
        catch
        {
            // A locked-down key must not abort the remaining types.
            return false;
        }
    }

    /// <summary>
    /// Removes a stale UserChoice key, and confirms it is gone.
    ///
    /// Windows protects the key with a Deny-SetValue rule, which is what stops an app
    /// forging the hash inside it. That rule also means the key cannot be opened for
    /// writing at all — so deleting it through any API that asks for write access on the
    /// key itself quietly does nothing. Deleting it through the parent works, because
    /// removing a subkey is a right on the parent.
    /// </summary>
    private static bool DeleteUserChoice(string extension)
    {
        DeleteChoiceRecord(extension, "UserChoice");

        // Never report success on trust: Windows may hold or restore this key.
        return string.IsNullOrEmpty(UserChoiceProgId(extension));
    }

    /// <summary>
    /// Puts Iris at the head of the extension's "open with" list — the ordinary
    /// most-recently-used list the shell keeps, which is not hash-protected and which
    /// Windows falls back on once no valid choice is recorded.
    /// </summary>
    private static void PromoteInOpenWithList(string extension)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                $@"{FileExtsKey}\{extension}\OpenWithList");

            var exeName = Path.GetFileName(FileAssociations.CurrentExePath);
            var slot = FindOrCreateSlot(key, exeName);

            if (slot is null) return;

            var order = key.GetValue("MRUList") as string ?? string.Empty;
            key.SetValue("MRUList", slot + order.Replace(slot, string.Empty));
        }
        catch
        {
            // Only costs Iris the fallback, so it is not worth failing the claim.
        }
    }

    /// <summary>Finds the letter already holding this program, or takes a free one.</summary>
    private static string? FindOrCreateSlot(RegistryKey key, string exeName)
    {
        foreach (var name in key.GetValueNames())
        {
            if (name == "MRUList") continue;

            if (string.Equals(key.GetValue(name) as string, exeName, StringComparison.OrdinalIgnoreCase))
                return name;
        }

        for (var letter = 'a'; letter <= 'z'; letter++)
        {
            var slot = letter.ToString();
            if (key.GetValue(slot) is not null) continue;

            key.SetValue(slot, exeName);
            return slot;
        }

        return null;
    }

    private static void DeleteChoiceRecord(string extension, string name)
    {
        try
        {
            using var parent = Registry.CurrentUser.OpenSubKey(
                $@"{FileExtsKey}\{extension}", writable: true);

            parent?.DeleteSubKey(name, throwOnMissingSubKey: false);
        }
        catch
        {
            // Leaving the record in place only means Windows keeps asking; it is not
            // worth failing the whole run over.
        }
    }

    /// <summary>
    /// Gives a type back to Windows: drops Iris's class default so the machine default,
    /// or the picker, decides again. Iris never re-adds a choice it removed, so this is
    /// the honest inverse of <see cref="Claim"/> rather than a full restore.
    /// </summary>
    public static void Release()
    {
        var changed = false;

        foreach (var type in MediaTypes.All)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    $@"Software\Classes\{type.Extension}", writable: true);

                if (key is null) continue;

                if (string.Equals(key.GetValue(null) as string, IrisProgId(type.Extension),
                        StringComparison.OrdinalIgnoreCase))
                {
                    key.DeleteValue(string.Empty, throwOnMissingValue: false);
                    changed = true;
                }
            }
            catch
            {
                // Best effort; releasing one type must not stop the others.
            }
        }

        if (changed) FileAssociations.NotifyShell();
    }

    // ==================================================================
    //  Registry reading
    // ==================================================================

    private static string IrisProgId(string extension) => "Iris" + extension;

    private static string? UserChoiceProgId(string extension)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                $@"{FileExtsKey}\{extension}\UserChoice");
            return key?.GetValue("ProgId") as string;
        }
        catch
        {
            return null;
        }
    }

    private const int AssocStrExecutable = 2;

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryStringW(
        int flags, int str, string assoc, string? extra, StringBuilder? result, ref int length);

    /// <summary>
    /// The program the shell would launch for this extension, exactly as a double-click
    /// resolves it. Comes back as OpenWith.exe when Windows would show the picker.
    /// </summary>
    private static string? ResolvedExecutable(string extension)
    {
        try
        {
            var length = 1024;
            var buffer = new StringBuilder(length);

            return AssocQueryStringW(0, AssocStrExecutable, extension, null, buffer, ref length) == 0
                ? buffer.ToString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True when the ProgId still resolves to a program present on disk.</summary>
    private static bool IsLive(string progId)
    {
        var command = OpenCommand(progId);
        if (string.IsNullOrWhiteSpace(command)) return false;

        var exe = ExecutableFrom(command);
        return !string.IsNullOrEmpty(exe) && File.Exists(exe);
    }

    private static string? OpenCommand(string progId)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
            return key?.GetValue(null) as string;
        }
        catch
        {
            return null;
        }
    }

    private static string? DescribeProgId(string progId)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(progId);
            var friendly = key?.GetValue("FriendlyTypeName") as string
                        ?? key?.GetValue(null) as string;

            if (!string.IsNullOrWhiteSpace(friendly)) return friendly;
        }
        catch
        {
            // Fall through to the raw ProgId, which is still better than nothing.
        }

        return progId;
    }

    /// <summary>Pulls the program out of a registered command line.</summary>
    private static string? ExecutableFrom(string command)
    {
        command = command.Trim();
        if (command.Length == 0) return null;

        if (command[0] == '"')
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }

        // Unquoted: the program ends at the first space, which is why Windows itself
        // cannot handle unquoted paths containing spaces either.
        var space = command.IndexOf(' ');
        return space < 0 ? command : command[..space];
    }
}
