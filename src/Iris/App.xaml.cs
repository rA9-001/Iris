using System.Windows;
using System.Windows.Threading;
using Iris.Interop;
using Iris.Localization;

namespace Iris;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LaunchTrace.Mark("App.OnStartup");

        var path = e.Args.FirstOrDefault();

        // "Iris.exe --set-default" runs the whole Windows hook-up without opening a
        // window, so it can be re-run after moving the exe. Exit code is the number of
        // file types Iris ends up owning.
        if (string.Equals(path, "--set-default", StringComparison.OrdinalIgnoreCase))
        {
            Shutdown(SetUpWithWindows());
            return;
        }

        // The other half of it: takes every key back out again. A portable app with no
        // installer needs a way to clean up that does not depend on opening a window.
        if (string.Equals(path, "--remove", StringComparison.OrdinalIgnoreCase))
        {
            Shutdown(RemoveFromWindows());
            return;
        }

        // One window, always. If Iris is already running, that window takes the file and
        // this process exits before it builds any UI at all.
        if (!SingleInstance.TryClaim(path))
        {
            LaunchTrace.Mark("handed to the running window");
            Shutdown();
            return;
        }

        // A playback glitch deep in libvlc should not take the whole player down.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // The file goes in through the constructor, not after Show(). Showing an empty
        // window first and loading afterwards is what made the start screen flash up.
        var window = new MainWindow(path);
        MainWindow = window;
        SingleInstance.FileReceived += window.OnSecondInstance;
        window.Show();
        LaunchTrace.Mark("Show() returned");
    }

    private static int SetUpWithWindows()
    {
        try
        {
            // No window is built on this path, so nothing else has applied the language
            // yet — and the names this writes into the registry are user-visible.
            Loc.Use(AppSettings.Load().Language);

            FileAssociations.Register();
            return DefaultApps.ClaimAvailable().Claimed;
        }
        catch
        {
            return 0;
        }
    }

    private static int RemoveFromWindows()
    {
        try
        {
            FileAssociations.Unregister();
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LaunchTrace.Mark("OnExit");
        SingleInstance.Stop();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.Message,
            "Iris",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }
}
