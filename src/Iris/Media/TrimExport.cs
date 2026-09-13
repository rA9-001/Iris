using System.IO;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Iris.Localization;

namespace Iris;

/// <summary>
/// Writes a time range of a video out as a new file.
///
/// This is Windows' own editing pipeline — the one the built-in player trims with — so
/// it is hardware accelerated and needs nothing shipped alongside it. libvlc can also
/// write a range, but only by stream copy, and a stream copy has to begin at a keyframe:
/// measured on a 2-minute capture, asking for 10 seconds gave back between 10.0 and
/// 16.2 depending on where the cut fell. That is useless for "grab this one moment", so
/// Iris re-encodes and asks for <see cref="MediaTrimmingPreference.Precise"/>, which
/// lands within about a frame.
/// </summary>
internal static class TrimExport
{
    /// <summary>The editing API arrived in Windows 10 2004. Older builds still play.</summary>
    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);

    /// <summary>Shortest clip worth writing; below this the handles are just noise.</summary>
    public static readonly TimeSpan MinimumLength = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Renders <paramref name="source"/> between the two offsets into
    /// <paramref name="destination"/>, reporting 0-100 as it goes.
    /// </summary>
    public static async Task ExportAsync(
        string source,
        string destination,
        TimeSpan start,
        TimeSpan end,
        IProgress<double>? progress,
        CancellationToken cancel)
    {
        if (!IsSupported)
        {
            throw new NotSupportedException(Loc.Get(TextKey.ExportNeedsNewerWindows));
        }

        // StorageFile rejects forward slashes outright, and Iris's own paths can carry
        // them, so everything is normalised before it crosses into WinRT.
        source = Path.GetFullPath(source);
        destination = Path.GetFullPath(destination);

        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(Loc.Get(TextKey.ExportSameFile));
        }

        var sourceFile = await StorageFile.GetFileFromPathAsync(source);
        var clip = await MediaClip.CreateFromFileAsync(sourceFile);

        var total = clip.OriginalDuration;
        start = Clamp(start, TimeSpan.Zero, total);
        end = Clamp(end, TimeSpan.Zero, total);

        if (end - start < MinimumLength)
        {
            throw new ArgumentException(Loc.Get(TextKey.ExportTooShort));
        }

        clip.TrimTimeFromStart = start;
        clip.TrimTimeFromEnd = total - end;

        var composition = new MediaComposition();
        composition.Clips.Add(clip);

        // Create the file only once the inputs are known good, so a failed export cannot
        // leave an empty file sitting where the user expected a clip.
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(destination)!);
        var destinationFile = await folder.CreateFileAsync(
            Path.GetFileName(destination), CreationCollisionOption.ReplaceExisting);

        // Matching the source's own encoding keeps the clip at the resolution, frame rate
        // and bitrate it was captured at. Without this the default profile re-encodes a
        // 6 Mbit capture down to about 4.5.
        // Only when the container is staying the same, though: handing the renderer a
        // profile describing an MKV while writing an .mp4 is asking for a failure.
        MediaEncodingProfile? profile = null;
        if (string.Equals(Path.GetExtension(source), Path.GetExtension(destination),
                          StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                profile = await MediaEncodingProfile.CreateFromFileAsync(sourceFile);
            }
            catch
            {
                // Nothing to do: the two-argument overload picks a sensible profile itself.
            }
        }

        try
        {
            var operation = profile is null
                ? composition.RenderToFileAsync(destinationFile, MediaTrimmingPreference.Precise)
                : composition.RenderToFileAsync(destinationFile, MediaTrimmingPreference.Precise, profile);

            var result = await operation.AsTask(cancel, new Progress<double>(p => progress?.Report(p)));

            if (result != TranscodeFailureReason.None)
            {
                throw new InvalidOperationException(Loc.Get(TextKey.ExportWriteFailed, result));
            }
        }
        catch (OperationCanceledException)
        {
            TryDelete(destination);
            throw;
        }
        catch
        {
            TryDelete(destination);
            throw;
        }
    }

    /// <summary>A half-written clip is worse than none: it looks like a real file.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // The renderer may still hold it briefly; leaving it is the lesser problem.
        }
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan low, TimeSpan high) =>
        value < low ? low : value > high ? high : value;

    /// <summary>"Clip.mp4" next to the original, without clobbering anything.</summary>
    public static string SuggestName(string sourcePath)
    {
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrEmpty(extension)) extension = ".mp4";

        var folder = Path.GetDirectoryName(sourcePath) ?? "";
        var clip = Loc.Get(TextKey.ClipWord);
        var candidate = Path.Combine(folder, $"{name} ({clip}){extension}");

        var n = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(folder, $"{name} ({clip} {n}){extension}");
            n++;
        }

        return Path.GetFileName(candidate);
    }
}
