namespace Iris.Localization;

/// <summary>
/// The reference language. Every other table is allowed to be incomplete and falls back
/// to this one, so nothing here may be missing.
/// </summary>
internal static class English
{
    public static readonly IReadOnlyDictionary<TextKey, string> Table = new Dictionary<TextKey, string>
    {
        // ---------- Start screen ----------
        [TextKey.StartDropHere] = "Drop a video here",
        [TextKey.StartOpenVideo] = "Open video",
        [TextKey.StartShortcuts] =
            "Space play  ·  ← → 5s  ·  J L 10s  ·  T trim  ·  F fullscreen  ·  O open",
        [TextKey.StartDefaultPlayerLink] = "Set Iris as your default player",
        [TextKey.StartSettingsLink] = "Settings",
        [TextKey.DropToPlay] = "Drop to play",

        // ---------- Control bar ----------
        [TextKey.TipBack10] = "Back 10 seconds  (J)",
        [TextKey.TipBack5] = "Back 5 seconds  (Left arrow)",
        [TextKey.TipPlayPause] = "Play / pause  (Space)",
        [TextKey.TipForward5] = "Forward 5 seconds  (Right arrow)",
        [TextKey.TipForward10] = "Forward 10 seconds  (L)",
        [TextKey.TipMute] = "Mute  (M)",
        [TextKey.TipTrim] = "Trim a clip out of this video  (T)",
        [TextKey.TipOpen] = "Open file  (O)",
        [TextKey.TipFullscreen] = "Fullscreen  (F)",
        [TextKey.TipSettings] = "Settings  (Ctrl+,)",

        // ---------- Folder navigation ----------
        [TextKey.NavPrevious] = "Previous",
        [TextKey.NavNext] = "Next",
        [TextKey.NavPosition] = "{0} of {1} in {2}",
        [TextKey.NavShortcutPrevious] = "Ctrl+←",
        [TextKey.NavShortcutNext] = "Ctrl+→",

        // ---------- Trimming ----------
        [TextKey.TrimTitle] = "Trim",
        [TextKey.TrimRangeLabel] = "{0} to {1}   ·   {2:0.0}s selected",
        [TextKey.TrimCancel] = "Cancel",
        [TextKey.TrimSaveClip] = "Save clip",
        [TextKey.TrimNothingToTrim] = "Nothing to trim",
        [TextKey.TrimTooCloseToEnd] = "Too close to the end",
        [TextKey.TrimTooCloseToStart] = "Too close to the start",
        [TextKey.TrimStartSet] = "Start set",
        [TextKey.TrimEndSet] = "End set",
        [TextKey.TrimNeedsNewerWindows] = "Trimming needs Windows 10 version 2004 or newer.",
        [TextKey.TrimWorksElsewhere] = "Everything else in Iris works on older builds.",
        [TextKey.TrimSaveDialogTitle] = "Save clip",
        [TextKey.TrimMp4Filter] = "MP4 video (*.mp4)|*.mp4|All files (*.*)|*.*",
        [TextKey.ExportSaving] = "Saving clip",
        [TextKey.ExportSaved] = "Clip saved",
        [TextKey.ExportCancelled] = "Cancelled",
        [TextKey.ExportDetail] = "{0:0.0} seconds to {1}",
        [TextKey.ExportFailed] = "Iris could not save the clip.",
        [TextKey.ExportWriteFailed] = "Windows could not write the clip ({0}).",
        [TextKey.ExportNeedsNewerWindows] =
            "Trimming needs Windows 10 version 2004 (build 19041) or newer.",
        [TextKey.ExportSameFile] =
            "Choose a different file: a clip cannot overwrite the video it came from.",
        [TextKey.ExportTooShort] = "The selection is too short to save.",
        // Goes into the suggested file name, as "holiday (clip).mp4".
        [TextKey.ClipWord] = "clip",

        // ---------- Playback ----------
        [TextKey.OsdMuted] = "Muted",
        [TextKey.OsdSnapshotCopied] = "Frame copied",
        [TextKey.OsdSnapshotFailed] = "Could not copy the frame",
        // "0.5×", "1×", "1.25×" — trailing zeros trimmed so normal speed is just "1×".
        [TextKey.SpeedLabel] = "{0:0.##}×",
        [TextKey.TipSpeed] = "Playback speed  (Ctrl+↑ / Ctrl+↓, Ctrl+0 to reset)",
        [TextKey.EndStop] = "Stop",
        [TextKey.EndRepeat] = "Repeat",
        [TextKey.EndPlayNext] = "Play next",
        [TextKey.ErrorHeading] = "Iris can’t play this file",
        [TextKey.ErrorBody] =
            "It may be damaged or incomplete, or in a format the player cannot decode.",
        [TextKey.ErrorOpenAnother] = "Open another video",
        [TextKey.OsdSkipForward] = "+{0:0}s",
        [TextKey.OsdSkipBack] = "{0:0}s",
        [TextKey.ErrorEngineStart] = "Iris could not start its playback engine.",
        [TextKey.OpenDialogTitle] = "Open video",

        // ---------- File dialog filter groups ----------
        [TextKey.FilterMedia] = "Media files",
        [TextKey.FilterVideo] = "Video files",
        [TextKey.FilterAudio] = "Audio files",
        [TextKey.FilterAll] = "All files",

        // ---------- Shown by Windows, not by Iris ----------
        [TextKey.AppDescription] = "Lightweight video player",
        [TextKey.ContextVerbPlay] = "Play with Iris",
        // The "&" marks the keyboard accelerator Explorer underlines.
        [TextKey.ContextVerbOpen] = "&Open with Iris",
        [TextKey.TypeVideoPattern] = "{0} Video",
        [TextKey.TypeAudioPattern] = "{0} Audio",
        [TextKey.TypeTransportStream] = "MPEG Transport Stream",
        [TextKey.TypeWindowsMediaVideo] = "Windows Media Video",
        [TextKey.TypeWindowsMediaAudio] = "Windows Media Audio",

        // ---------- Windows integration dialog ----------
        [TextKey.AssocOfferHeading] = "Set Iris up",
        [TextKey.AssocOfferBody] =
            "Iris will open video and audio files ({0} video and {1} audio formats): it is " +
            "added to the Windows “Open with” menu, gets a “Play with Iris” entry on " +
            "right-click, and becomes the default player for every type Windows lets it take.",
        [TextKey.AssocOfferNote] =
            "Types another installed app is already set to are left alone — Windows reserves " +
            "those for you to change. This writes to your own user account only, and the " +
            "button below undoes all of it.",
        [TextKey.AssocSetUp] = "Set up Iris",
        [TextKey.AssocNotNow] = "Not now",
        [TextKey.AssocReadyHeading] = "Iris is ready to use",
        [TextKey.AssocReadyBodyNone] =
            "Iris is in the “Open with” menu, but it is not opening anything by default yet.",
        [TextKey.AssocReadyBody] =
            "Iris is the default for {0} of {1} file types, so double-clicking one opens it here.",
        [TextKey.AssocUnassignedOne] = "{0} is still unassigned. Iris can take it now.",
        [TextKey.AssocUnassignedMany] = "{0} are still unassigned. Iris can take those now.",
        [TextKey.AssocMakeDefault] = "Make Iris the default",
        [TextKey.AssocTakenOne] =
            "{0} is set to {1}. Windows only lets you change that yourself — the button " +
            "below opens the page where you can.",
        [TextKey.AssocTakenMany] =
            "{0} are set to {1}. Windows only lets you change that yourself — the button " +
            "below opens the page where you can.",
        [TextKey.AssocAnotherApp] = "another app",
        [TextKey.AssocChooseDefaults] = "Choose default apps",
        [TextKey.AssocAllSet] = "Every type Iris handles opens here. Nothing else to set up.",
        [TextKey.AssocClose] = "Close",
        [TextKey.AssocRemove] = "Remove Iris",
        [TextKey.AssocAndMore] = "{0} and {1} more",
        [TextKey.AssocErrorRegister] = "Iris could not register itself with Windows.",
        [TextKey.AssocErrorRemove] = "Iris could not remove its file associations.",
        [TextKey.AssocErrorDefault] = "Iris could not set itself as the default player.",

        // ---------- Settings ----------
        [TextKey.SettingsTitle] = "Settings",
        [TextKey.SettingsLanguage] = "Language",
        [TextKey.SettingsWhenFileEnds] = "When a file ends",
        [TextKey.SettingsWhenFileEndsNote] =
            "Repeat loops the same file. Play next continues with the next file in the "
            + "folder, in the order the arrows use. Press R to change this while watching.",
        [TextKey.SettingsLanguageNote] = "Applies straight away — nothing needs restarting.",
        [TextKey.SettingsIntegration] = "Windows integration",
        [TextKey.SettingsIntegrationNote] =
            "The “Open with” menu, the right-click entry, and which files open in Iris.",
        [TextKey.SettingsIntegrationButton] = "Manage",
        [TextKey.SettingsClose] = "Close",
    };
}
