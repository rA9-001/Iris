namespace Iris.Localization;

/// <summary>
/// Every piece of text Iris shows. Using an enum rather than loose strings means a typo
/// is a build error in code and a load error in XAML, and it makes a missing translation
/// findable by comparing this list against a language table.
/// </summary>
public enum TextKey
{
    // ---------- Start screen ----------
    StartDropHere,
    StartOpenVideo,
    StartShortcuts,
    StartDefaultPlayerLink,
    StartSettingsLink,
    DropToPlay,

    // ---------- Control bar ----------
    TipBack10,
    TipBack5,
    TipPlayPause,
    TipForward5,
    TipForward10,
    TipMute,
    TipTrim,
    TipOpen,
    TipFullscreen,
    TipSettings,

    // ---------- Folder navigation ----------
    NavPrevious,
    NavNext,
    NavPosition,
    NavShortcutPrevious,
    NavShortcutNext,

    // ---------- Trimming ----------
    TrimTitle,
    TrimRangeLabel,
    TrimCancel,
    TrimSaveClip,
    TrimNothingToTrim,
    TrimTooCloseToEnd,
    TrimTooCloseToStart,
    TrimStartSet,
    TrimEndSet,
    TrimNeedsNewerWindows,
    TrimWorksElsewhere,
    TrimSaveDialogTitle,
    TrimMp4Filter,
    ExportSaving,
    ExportSaved,
    ExportCancelled,
    ExportDetail,
    ExportFailed,
    ExportWriteFailed,
    ExportNeedsNewerWindows,
    ExportSameFile,
    ExportTooShort,
    ClipWord,

    // ---------- Playback ----------
    OsdMuted,
    OsdSnapshotCopied,
    OsdSnapshotFailed,
    SpeedLabel,
    TipSpeed,
    EndStop,
    EndRepeat,
    EndPlayNext,

    // ---------- A file that will not play ----------
    ErrorHeading,
    ErrorBody,
    ErrorOpenAnother,

    OsdSkipForward,
    OsdSkipBack,
    ErrorEngineStart,
    OpenDialogTitle,

    // ---------- File dialog filter groups ----------
    FilterMedia,
    FilterVideo,
    FilterAudio,
    FilterAll,

    // ---------- Shown by Windows, not by Iris: the registered names ----------
    AppDescription,
    ContextVerbPlay,
    ContextVerbOpen,
    TypeVideoPattern,
    TypeAudioPattern,
    TypeTransportStream,
    TypeWindowsMediaVideo,
    TypeWindowsMediaAudio,

    // ---------- Windows integration dialog ----------
    AssocOfferHeading,
    AssocOfferBody,
    AssocOfferNote,
    AssocSetUp,
    AssocNotNow,
    AssocReadyHeading,
    AssocReadyBodyNone,
    AssocReadyBody,
    AssocUnassignedOne,
    AssocUnassignedMany,
    AssocMakeDefault,
    AssocTakenOne,
    AssocTakenMany,
    AssocAnotherApp,
    AssocChooseDefaults,
    AssocAllSet,
    AssocClose,
    AssocRemove,
    AssocAndMore,
    AssocErrorRegister,
    AssocErrorRemove,
    AssocErrorDefault,

    // ---------- Settings ----------
    SettingsTitle,
    SettingsLanguage,
    SettingsWhenFileEnds,
    SettingsWhenFileEndsNote,
    SettingsLanguageNote,
    SettingsIntegration,
    SettingsIntegrationNote,
    SettingsIntegrationButton,
    SettingsClose,
}
