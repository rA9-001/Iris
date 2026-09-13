using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Iris.Localization;

namespace Iris;

/// <summary>What happens when playback reaches the end of a file.</summary>
public enum EndAction
{
    /// <summary>Stay on the last frame. Pressing play starts the file again.</summary>
    Stop,

    /// <summary>Loop the same file.</summary>
    Repeat,

    /// <summary>Move on to the next file in the folder, in the order the arrows use.</summary>
    PlayNext,
}

/// <summary>
/// User state that should survive a restart. Deliberately small: anything that can be
/// recomputed or that the user would not miss does not belong here.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Percent, where 100 is the untouched signal and anything above it is amplified by
    /// the player. Ranges to <see cref="MaxVolume"/>.
    /// </summary>
    public int Volume { get; set; } = 100;
    public bool Muted { get; set; }

    public EndAction WhenFileEnds { get; set; } = EndAction.Stop;

    /// <summary>Loud enough to rescue a quiet recording; libvlc will not go above this.</summary>
    public const int MaxVolume = 200;
    public string? LastFolder { get; set; }

    /// <summary>Iris starts in English until someone chooses otherwise.</summary>
    public AppLanguage Language { get; set; } = AppLanguage.English;

    public double WindowWidth { get; set; } = 1120;
    public double WindowHeight { get; set; } = 670;
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public bool Maximized { get; set; }

    /// <summary>Set once the first-run association offer has been made, so it is asked
    /// exactly once however the user answered it.</summary>
    public bool AssociationPromptShown { get; set; }

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Iris");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize(json, SettingsJson.Default.AppSettings);
                if (loaded is not null)
                {
                    loaded.Volume = Math.Clamp(loaded.Volume, 0, MaxVolume);
                    return loaded;
                }
            }
        }
        catch
        {
            // A corrupt or unreadable settings file must never stop the player from starting.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(this, SettingsJson.Default.AppSettings);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Losing preferences is not worth surfacing an error dialog over.
        }
    }
}

// Language is written as "English" / "German" rather than 0 / 1, so the file stays
// readable and an unknown value degrades to the default instead of a wrong language.
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJson : JsonSerializerContext;
