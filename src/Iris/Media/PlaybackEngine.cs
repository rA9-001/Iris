using System.IO;
using LibVLCSharp.Shared;

using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace Iris;

/// <summary>
/// Owns libvlc: starting it, handing out the one media player, and tearing it down.
///
/// None of this needs a window, and keeping it out of one means the whole cost of
/// standing libvlc up — which dominates a cold launch — is described in a single place
/// rather than threaded through the shell.
/// </summary>
internal sealed class PlaybackEngine
{
    private Task<LibVLC>? _starting;

    /// <summary>The engine, once it has finished starting.</summary>
    public LibVLC? Vlc { get; private set; }

    /// <summary>The one player, created on first use.</summary>
    public MediaPlayer? Player { get; private set; }

    /// <summary>
    /// Begins building libvlc on a worker, and returns the same task on every later call.
    ///
    /// This used to run inline in the window constructor, which meant scanning 300-odd
    /// plugin DLLs before the window could be shown at all — the single largest block of
    /// launch time, and far worse on a cold disk. Started from a worker at the top of
    /// construction it overlaps the time WPF needs anyway, and is ready well before there
    /// is a window to play into.
    /// </summary>
    public Task<LibVLC> Start()
    {
        return _starting ??= Task.Run(() =>
        {
            // Runs alongside the scan below rather than before it: libvlc works through
            // the plugins one at a time, so warming the rest in parallel means it keeps
            // finding the next file already in memory.
            _ = Task.Run(PrewarmPlugins);

            Core.Initialize();
            LaunchTrace.Mark("Core.Initialize (worker)");

            var vlc = new LibVLC(
                "--no-video-title-show",   // we draw our own title
                "--no-osd",                // and our own OSD
                "--no-snapshot-preview",
                "--quiet",
                "--file-caching=500",
                "--network-caching=1000");

            LaunchTrace.Mark("LibVLC ready (worker)");
            return vlc;
        });
    }

    /// <summary>
    /// Waits for the engine and returns the player, creating it the first time. Throws
    /// only if the engine itself could not be built; the caller decides how to say so.
    /// </summary>
    public async Task<MediaPlayer> GetPlayerAsync()
    {
        if (Player is not null) return Player;

        var vlc = await Start().ConfigureAwait(true);

        // A second caller may have won the race while this one awaited.
        if (Player is not null) return Player;

        Vlc = vlc;
        Player = new MediaPlayer(vlc)
        {
            // Iris owns all input; letting libvlc grab keys or clicks fights our shortcuts.
            EnableKeyInput = false,
            EnableMouseInput = false,
        };

        LaunchTrace.Mark("player created");
        return Player;
    }

    /// <summary>
    /// Tears libvlc down without blocking the caller.
    ///
    /// Disposing a media player re-enters libvlc's own threads, which can take a moment
    /// or hang outright, so this never happens on the way out of the UI thread. Nothing
    /// waits on the result: the process is leaving.
    /// </summary>
    public void ShutDownInBackground()
    {
        var player = Player;
        var vlc = Vlc;
        var pending = _starting;

        Player = null;
        Vlc = null;

        Task.Run(() =>
        {
            try
            {
                player?.Stop();
                player?.Dispose();
                vlc?.Dispose();

                // Closed mid-launch: the engine may still be arriving on a worker, and an
                // unowned LibVLC would keep native threads alive.
                if (vlc is null && pending is { IsCompletedSuccessfully: true })
                {
                    pending.Result.Dispose();
                }
            }
            catch
            {
                // Nothing useful to do while the process is on its way out.
            }
        });
    }

    /// <summary>
    /// Reads every plugin DLL on many threads so the operating system — and, far more
    /// expensively, the on-access virus scanner — does that work concurrently.
    ///
    /// libvlc has no plugin cache here (the VideoLAN package ships none, and libvlc only
    /// ever reads one, never writes it), so every start dlopens all 323 plugin DLLs, one
    /// after another. Warm that costs about 300ms and nobody notices. Cold — a fresh
    /// download, or the first launch after a reboot — each of those opens is a separate
    /// synchronous scan, measured at over ten seconds before any video could appear. The
    /// files are needed either way; the only thing wrong with it was waiting for them one
    /// at a time.
    /// </summary>
    private static void PrewarmPlugins()
    {
        try
        {
            var plugins = FindPluginFolder();
            if (plugins is null) return;

            var files = Directory.GetFiles(plugins, "*.dll", SearchOption.AllDirectories);
            ReadInParallel(files);

            LaunchTrace.Mark($"plugins prewarmed ({files.Length} files)");
        }
        catch
        {
            // Prewarming is an optimisation. Failing it must never stop playback.
        }
    }

    private static string? FindPluginFolder()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "libvlc");
        if (!Directory.Exists(root)) return null;

        return Directory
            .EnumerateDirectories(root, "plugins", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private static void ReadInParallel(string[] files)
    {
        if (files.Length == 0) return;

        // Dedicated threads rather than the thread pool: every one of these blocks on the
        // scanner, and the pool deliberately adds blocked threads back only about one per
        // second, which throttles exactly the parallelism this needs. The work is waiting,
        // not computing, so it is worth heavily oversubscribing the CPU. Measured on 20
        // cores: 10.4s at one thread, 2.1s at 8, 1.8s at 16, 1.6s at 32, 1.4s at 64.
        var next = -1;
        var workers = new Thread[Math.Clamp(Environment.ProcessorCount * 3, 16, 64)];

        for (var i = 0; i < workers.Length; i++)
        {
            workers[i] = new Thread(() =>
            {
                var buffer = new byte[1 << 16];
                int index;
                while ((index = Interlocked.Increment(ref next)) < files.Length)
                {
                    ReadWhole(files[index], buffer);
                }
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
            };

            workers[i].Start();
        }

        foreach (var worker in workers) worker.Join();
    }

    private static void ReadWhole(string file, byte[] buffer)
    {
        try
        {
            using var stream = new FileStream(
                file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 1 << 16, FileOptions.SequentialScan);

            while (stream.Read(buffer) > 0) { }
        }
        catch
        {
            // A plugin we cannot read is libvlc's problem to report, not ours.
        }
    }
}
