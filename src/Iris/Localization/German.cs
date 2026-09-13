namespace Iris.Localization;

/// <summary>
/// German. Uses the formal "Sie" on the few lines that address the reader at all, and
/// German quotation marks, so it reads like a German program rather than a translated one.
/// </summary>
internal static class German
{
    public static readonly IReadOnlyDictionary<TextKey, string> Table = new Dictionary<TextKey, string>
    {
        // ---------- Start screen ----------
        [TextKey.StartDropHere] = "Video hier ablegen",
        [TextKey.StartOpenVideo] = "Video öffnen",
        [TextKey.StartShortcuts] =
            "Leertaste Wiedergabe  ·  ← → 5 s  ·  J L 10 s  ·  T Zuschneiden  ·  F Vollbild  ·  O Öffnen",
        [TextKey.StartDefaultPlayerLink] = "Iris als Standardplayer festlegen",
        [TextKey.StartSettingsLink] = "Einstellungen",
        [TextKey.DropToPlay] = "Zum Abspielen ablegen",

        // ---------- Control bar ----------
        [TextKey.TipBack10] = "10 Sekunden zurück  (J)",
        [TextKey.TipBack5] = "5 Sekunden zurück  (Pfeil links)",
        [TextKey.TipPlayPause] = "Wiedergabe / Pause  (Leertaste)",
        [TextKey.TipForward5] = "5 Sekunden vor  (Pfeil rechts)",
        [TextKey.TipForward10] = "10 Sekunden vor  (L)",
        [TextKey.TipMute] = "Stummschalten  (M)",
        [TextKey.TipTrim] = "Ausschnitt aus diesem Video erstellen  (T)",
        [TextKey.TipOpen] = "Datei öffnen  (O)",
        [TextKey.TipFullscreen] = "Vollbild  (F)",
        [TextKey.TipSettings] = "Einstellungen  (Strg+,)",

        // ---------- Folder navigation ----------
        [TextKey.NavPrevious] = "Vorheriges",
        [TextKey.NavNext] = "Nächstes",
        [TextKey.NavPosition] = "{0} von {1} in {2}",
        [TextKey.NavShortcutPrevious] = "Strg+←",
        [TextKey.NavShortcutNext] = "Strg+→",

        // ---------- Trimming ----------
        [TextKey.TrimTitle] = "Zuschneiden",
        [TextKey.TrimRangeLabel] = "{0} bis {1}   ·   {2:0.0} s ausgewählt",
        [TextKey.TrimCancel] = "Abbrechen",
        [TextKey.TrimSaveClip] = "Ausschnitt speichern",
        [TextKey.TrimNothingToTrim] = "Nichts zum Zuschneiden",
        [TextKey.TrimTooCloseToEnd] = "Zu nah am Ende",
        [TextKey.TrimTooCloseToStart] = "Zu nah am Anfang",
        [TextKey.TrimStartSet] = "Anfang gesetzt",
        [TextKey.TrimEndSet] = "Ende gesetzt",
        [TextKey.TrimNeedsNewerWindows] =
            "Zum Zuschneiden wird Windows 10 Version 2004 oder neuer benötigt.",
        [TextKey.TrimWorksElsewhere] = "Alles andere in Iris funktioniert auch mit älteren Versionen.",
        [TextKey.TrimSaveDialogTitle] = "Ausschnitt speichern",
        [TextKey.TrimMp4Filter] = "MP4-Video (*.mp4)|*.mp4|Alle Dateien (*.*)|*.*",
        [TextKey.ExportSaving] = "Ausschnitt wird gespeichert",
        [TextKey.ExportSaved] = "Ausschnitt gespeichert",
        [TextKey.ExportCancelled] = "Abgebrochen",
        [TextKey.ExportDetail] = "{0:0.0} Sekunden in {1}",
        [TextKey.ExportFailed] = "Iris konnte den Ausschnitt nicht speichern.",
        [TextKey.ExportWriteFailed] = "Windows konnte den Ausschnitt nicht schreiben ({0}).",
        [TextKey.ExportNeedsNewerWindows] =
            "Zum Zuschneiden wird Windows 10 Version 2004 (Build 19041) oder neuer benötigt.",
        [TextKey.ExportSameFile] =
            "Bitte eine andere Datei wählen: Ein Ausschnitt kann das Video, aus dem er stammt, "
            + "nicht überschreiben.",
        [TextKey.ExportTooShort] = "Die Auswahl ist zu kurz zum Speichern.",
        [TextKey.ClipWord] = "Ausschnitt",

        // ---------- Playback ----------
        [TextKey.OsdMuted] = "Stumm",
        [TextKey.OsdSnapshotCopied] = "Bild kopiert",
        [TextKey.OsdSnapshotFailed] = "Bild konnte nicht kopiert werden",
        [TextKey.SpeedLabel] = "{0:0.##}×",
        [TextKey.TipSpeed] = "Wiedergabegeschwindigkeit  (Strg+↑ / Strg+↓, Strg+0 setzt zurück)",
        [TextKey.EndStop] = "Anhalten",
        [TextKey.EndRepeat] = "Wiederholen",
        [TextKey.EndPlayNext] = "Nächste abspielen",
        [TextKey.ErrorHeading] = "Iris kann diese Datei nicht abspielen",
        [TextKey.ErrorBody] =
            "Sie ist möglicherweise beschädigt oder unvollständig, oder liegt in einem Format "
            + "vor, das der Player nicht dekodieren kann.",
        [TextKey.ErrorOpenAnother] = "Anderes Video öffnen",
        [TextKey.OsdSkipForward] = "+{0:0} s",
        [TextKey.OsdSkipBack] = "{0:0} s",
        [TextKey.ErrorEngineStart] = "Iris konnte die Wiedergabe-Engine nicht starten.",
        [TextKey.OpenDialogTitle] = "Video öffnen",

        // ---------- File dialog filter groups ----------
        [TextKey.FilterMedia] = "Mediendateien",
        [TextKey.FilterVideo] = "Videodateien",
        [TextKey.FilterAudio] = "Audiodateien",
        [TextKey.FilterAll] = "Alle Dateien",

        // ---------- Shown by Windows, not by Iris ----------
        [TextKey.AppDescription] = "Schlanker Videoplayer",
        [TextKey.ContextVerbPlay] = "Mit Iris abspielen",
        [TextKey.ContextVerbOpen] = "Mit Iris &öffnen",
        // German compounds these with a hyphen: "MP4-Video", not "MP4 Video".
        [TextKey.TypeVideoPattern] = "{0}-Video",
        [TextKey.TypeAudioPattern] = "{0}-Audio",
        [TextKey.TypeTransportStream] = "MPEG-Transportstrom",
        // Left unhyphenated: it is Microsoft's own product name, and the pattern would
        // produce "Windows Media-Video", where the hyphen binds the wrong two words.
        [TextKey.TypeWindowsMediaVideo] = "Windows Media Video",
        [TextKey.TypeWindowsMediaAudio] = "Windows Media Audio",

        // ---------- Windows integration dialog ----------
        [TextKey.AssocOfferHeading] = "Iris einrichten",
        [TextKey.AssocOfferBody] =
            "Iris öffnet dann Video- und Audiodateien ({0} Video- und {1} Audioformate): Iris " +
            "erscheint im Windows-Menü „Öffnen mit“, erhält beim Rechtsklick den Eintrag " +
            "„Mit Iris abspielen“ und wird zum Standardplayer für jeden Dateityp, den Windows " +
            "freigibt.",
        [TextKey.AssocOfferNote] =
            "Dateitypen, für die bereits eine andere installierte App festgelegt ist, bleiben " +
            "unverändert – die darf nur Windows selbst ändern. Geschrieben wird ausschließlich " +
            "in Ihr eigenes Benutzerkonto, und die Schaltfläche unten macht alles rückgängig.",
        [TextKey.AssocSetUp] = "Iris einrichten",
        [TextKey.AssocNotNow] = "Jetzt nicht",
        [TextKey.AssocReadyHeading] = "Iris ist einsatzbereit",
        [TextKey.AssocReadyBodyNone] =
            "Iris steht im Menü „Öffnen mit“, öffnet aber noch keine Dateien automatisch.",
        [TextKey.AssocReadyBody] =
            "Iris ist Standard für {0} von {1} Dateitypen – ein Doppelklick öffnet sie hier.",
        [TextKey.AssocUnassignedOne] =
            "{0} ist noch keiner App zugewiesen. Iris kann diesen Typ jetzt übernehmen.",
        [TextKey.AssocUnassignedMany] =
            "{0} sind noch keiner App zugewiesen. Iris kann diese Typen jetzt übernehmen.",
        [TextKey.AssocMakeDefault] = "Iris als Standard festlegen",
        [TextKey.AssocTakenOne] =
            "{0} ist auf {1} festgelegt. Das können nur Sie selbst ändern – die Schaltfläche " +
            "unten öffnet die passende Seite.",
        [TextKey.AssocTakenMany] =
            "{0} sind auf {1} festgelegt. Das können nur Sie selbst ändern – die Schaltfläche " +
            "unten öffnet die passende Seite.",
        [TextKey.AssocAnotherApp] = "eine andere App",
        [TextKey.AssocChooseDefaults] = "Standard-Apps wählen",
        [TextKey.AssocAllSet] =
            "Alle von Iris unterstützten Dateitypen werden hier geöffnet. Nichts weiter zu tun.",
        [TextKey.AssocClose] = "Schließen",
        [TextKey.AssocRemove] = "Iris entfernen",
        [TextKey.AssocAndMore] = "{0} und {1} weitere",
        [TextKey.AssocErrorRegister] = "Iris konnte sich nicht bei Windows registrieren.",
        [TextKey.AssocErrorRemove] = "Iris konnte die Dateizuordnungen nicht entfernen.",
        [TextKey.AssocErrorDefault] = "Iris konnte sich nicht als Standardplayer festlegen.",

        // ---------- Settings ----------
        [TextKey.SettingsTitle] = "Einstellungen",
        [TextKey.SettingsLanguage] = "Sprache",
        [TextKey.SettingsWhenFileEnds] = "Wenn eine Datei endet",
        [TextKey.SettingsWhenFileEndsNote] =
            "„Wiederholen“ spielt dieselbe Datei erneut ab. „Nächste abspielen“ fährt mit der "
            + "nächsten Datei im Ordner fort, in der Reihenfolge der Pfeiltasten. Mit R lässt "
            + "sich das während der Wiedergabe umschalten.",
        [TextKey.SettingsLanguageNote] = "Wird sofort übernommen – kein Neustart nötig.",
        [TextKey.SettingsIntegration] = "Windows-Integration",
        [TextKey.SettingsIntegrationNote] =
            "Das Menü „Öffnen mit“, der Rechtsklick-Eintrag und welche Dateien in Iris geöffnet werden.",
        [TextKey.SettingsIntegrationButton] = "Verwalten",
        [TextKey.SettingsClose] = "Schließen",
    };
}
