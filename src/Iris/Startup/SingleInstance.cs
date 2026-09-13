using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows;
using Iris.Interop;

namespace Iris;

/// <summary>
/// Keeps Iris to a single window.
///
/// Double-clicking a second video in Explorer starts a second Iris process — Windows has
/// no idea the first one is running. The first process to start claims a mutex and opens
/// a named pipe; every later one finds the mutex taken, posts its file down the pipe and
/// exits before it ever builds a UI. The running window then takes the file over.
/// </summary>
internal static class SingleInstance
{
    /// <summary>Raised on the UI thread when another launch hands its file over. The
    /// argument is null when Iris was started again with no file.</summary>
    public static event Action<string?>? FileReceived;

    /// <summary>Held for the lifetime of the process; closing the handle releases it.</summary>
    private static Mutex? _claim;
    private static CancellationTokenSource? _stopping;
    private static string? _pipeName;

    /// <summary>
    /// The mutex lives in the session-local namespace, so a second desktop session gets
    /// its own player rather than fighting over one window it cannot see. Pipe names have
    /// no such namespace, so the session id goes into the name by hand.
    /// </summary>
    private static string PipeName => _pipeName ??=
        $"Iris.Instance.{Process.GetCurrentProcess().SessionId}";

    /// <summary>
    /// Returns true if this process should go on to open a window, false if it has handed
    /// its file to an instance that is already running and should now exit.
    /// </summary>
    public static bool TryClaim(string? path)
    {
        bool first;
        try
        {
            _claim = new Mutex(initiallyOwned: true, @"Local\Iris.SingleInstance", out first);
        }
        catch
        {
            // No mutex means no coordination, but a player that starts is better than one
            // that does not.
            return true;
        }

        if (first)
        {
            StartListening();
            return true;
        }

        if (Send(path))
        {
            _claim.Dispose();
            _claim = null;
            return false;
        }

        // The other instance never answered — it may be part-way through shutting down,
        // or wedged. A second window is not what was asked for, but silently dropping the
        // file the user just double-clicked is worse, so carry on as a normal launch.
        _claim.Dispose();
        _claim = null;
        return true;
    }

    /// <summary>Releases the claim and stops the listener. Called from App.OnExit.</summary>
    public static void Stop()
    {
        try { _stopping?.Cancel(); } catch { }
        try { _claim?.Dispose(); } catch { }
        _claim = null;
    }

    private static void StartListening()
    {
        _stopping = new CancellationTokenSource();
        var token = _stopping.Token;

        // On a worker: waiting for a connection must never sit in front of the window.
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                    using var reader = new StreamReader(server, new UTF8Encoding(false));
                    var payload = (await reader.ReadToEndAsync(token).ConfigureAwait(false)).Trim();

                    var handler = FileReceived;
                    if (handler is null) continue;

                    var path = payload.Length == 0 ? null : payload;
                    Application.Current?.Dispatcher.BeginInvoke(() => handler(path));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    // A connection that broke half-way is not worth ending the loop over;
                    // the next launch gets a fresh pipe on the next pass.
                }
            }
        }, token);
    }

    private static bool Send(string? path)
    {
        // Two launches within a few milliseconds of each other can get here before the
        // first one's listener is up, so retry rather than giving up on the first refusal.
        var deadline = Environment.TickCount64 + 3000;

        do
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(250);

                // Windows only lets the process that holds the foreground give it away.
                // Without this the running window would take the file and stay buried.
                Native.GrantForegroundRights();

                using (var writer = new StreamWriter(client, new UTF8Encoding(false)))
                {
                    writer.Write(path ?? string.Empty);
                    writer.Flush();
                }

                return true;
            }
            catch (TimeoutException)
            {
                // Nobody listening yet.
            }
            catch (IOException)
            {
                // Pipe busy: another launch is being served right now.
            }
            catch
            {
                return false;
            }
        }
        while (Environment.TickCount64 < deadline);

        return false;
    }
}
