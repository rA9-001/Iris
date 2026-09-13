using System.IO;
using Iris.Localization;

namespace Iris;

/// <summary>
/// The file types Iris claims. One table drives the open dialog, drag-and-drop
/// filtering and the Windows registration, so they can never drift apart.
/// </summary>
public static class MediaTypes
{
    /// <summary>
    /// One media type. <paramref name="Format"/> is the bare format name, which is the
    /// same in every language; the word "Video" or "Audio" around it is not, so the
    /// display name is composed rather than stored. <paramref name="NameKey"/> covers the
    /// odd one out that is not "&lt;format&gt; Video".
    /// </summary>
    public sealed record MediaType(
        string Extension, string Format, bool IsVideo, TextKey? NameKey = null)
    {
        /// <summary>
        /// What Explorer and Settings show for this type. Read at registration time, so
        /// it follows whatever language Iris was registered in.
        /// </summary>
        public string FriendlyName => NameKey is { } key
            ? Loc.Get(key)
            : Loc.Get(IsVideo ? TextKey.TypeVideoPattern : TextKey.TypeAudioPattern, Format);
    }

    public static readonly MediaType[] All =
    [
        new(".mp4",  "MP4",           true),
        new(".mkv",  "Matroska",      true),
        new(".avi",  "AVI",           true),
        new(".mov",  "QuickTime",     true),
        new(".wmv",  "Windows Media", true,  TextKey.TypeWindowsMediaVideo),
        new(".webm", "WebM",          true),
        new(".m4v",  "MPEG-4",        true),
        new(".flv",  "Flash",         true),
        new(".mpg",  "MPEG",          true),
        new(".mpeg", "MPEG",          true),
        new(".ts",   "MPEG",          true,  TextKey.TypeTransportStream),
        new(".m2ts", "Blu-ray",       true),
        new(".3gp",  "3GPP",          true),
        new(".ogv",  "Ogg",           true),
        new(".vob",  "DVD",           true),
        new(".divx", "DivX",          true),

        new(".mp3",  "MP3",           false),
        new(".m4a",  "MPEG-4",        false),
        new(".flac", "FLAC",          false),
        new(".wav",  "WAV",           false),
        new(".aac",  "AAC",           false),
        new(".ogg",  "Ogg",           false),
        new(".opus", "Opus",          false),
        new(".wma",  "Windows Media", false, TextKey.TypeWindowsMediaAudio),
    ];

    public static readonly string[] Extensions = All.Select(t => t.Extension).ToArray();

    public static int VideoCount => All.Count(t => t.IsVideo);
    public static int AudioCount => All.Count(t => !t.IsVideo);

    public static bool IsSupported(string path) =>
        Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Filter string for the open dialog, grouped so video leads.</summary>
    public static string DialogFilter
    {
        get
        {
            var media = string.Join(";", Extensions.Select(x => "*" + x));
            var video = string.Join(";", All.Where(t => t.IsVideo).Select(x => "*" + x.Extension));
            var audio = string.Join(";", All.Where(t => !t.IsVideo).Select(x => "*" + x.Extension));
            return $"{Loc.Get(TextKey.FilterMedia)}|{media}" +
                   $"|{Loc.Get(TextKey.FilterVideo)}|{video}" +
                   $"|{Loc.Get(TextKey.FilterAudio)}|{audio}" +
                   $"|{Loc.Get(TextKey.FilterAll)}|*.*";
        }
    }
}
