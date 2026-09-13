using System.Diagnostics;
using System.IO;

namespace Iris;

/// <summary>
/// Millisecond timings for the launch path, written only when IRIS_TRACE=1 so it costs
/// nothing in normal use. Startup regressions are invisible without numbers.
/// </summary>
internal static class LaunchTrace
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("IRIS_TRACE") == "1";

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object Gate = new();
    private static string? _path;

    /// <summary>Resolved once: it cannot change, and opening a process handle on every
    /// mark made the cheapest thing in here the most expensive.</summary>
    private static readonly DateTime ProcessStart = ResolveProcessStart();

    private static DateTime ResolveProcessStart()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            return self.StartTime;
        }
        catch
        {
            // Without the real figure, measure from the first time anything asked.
            return DateTime.Now - Clock.Elapsed;
        }
    }

    /// <summary>Milliseconds from process start to now, including runtime startup.</summary>
    public static double SinceProcessStart => (DateTime.Now - ProcessStart).TotalMilliseconds;

    /// <summary>Lets callers skip building a message when nothing is being recorded.</summary>
    public static bool Tracing => Enabled;

    public static void Mark(string stage)
    {
        if (!Enabled) return;

        try
        {
            lock (Gate)
            {
                _path ??= Path.Combine(
                    Path.GetTempPath(),
                    $"iris-trace-{Environment.ProcessId}.log");

                File.AppendAllText(_path, $"{SinceProcessStart,8:F0}ms  {stage}\n");
            }
        }
        catch
        {
            // Tracing must never be able to break a launch.
        }
    }
}
