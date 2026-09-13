using System.Windows;
using System.Windows.Media;
using Iris.Interop;
using Iris.Localization;

namespace Iris;

/// <summary>
/// Iris's settings. Small on purpose — the player has very little worth configuring, and
/// anything that belongs to Windows rather than to Iris is handed to the integration
/// dialog instead of duplicated here.
/// </summary>
public partial class SettingsDialog : Window
{
    /// <summary>
    /// Set while the language buttons are being put into their starting state, so
    /// restoring the current choice does not read as the user making it.
    /// </summary>
    private bool _applying;

    private readonly AppSettings _settings;

    public SettingsDialog(Window owner, AppSettings settings)
    {
        InitializeComponent();
        Owner = owner;
        _settings = settings;

        SourceInitialized += (_, _) =>
            Native.ApplyDarkChrome(this, (Color)FindResource("BackgroundColor"));

        _applying = true;
        EnglishOption.IsChecked = Loc.Language == AppLanguage.English;
        GermanOption.IsChecked = Loc.Language == AppLanguage.German;

        EndStopOption.IsChecked = _settings.WhenFileEnds == EndAction.Stop;
        EndRepeatOption.IsChecked = _settings.WhenFileEnds == EndAction.Repeat;
        EndNextOption.IsChecked = _settings.WhenFileEnds == EndAction.PlayNext;
        _applying = false;
    }

    private void OnEndStopChecked(object sender, RoutedEventArgs e) => Choose(EndAction.Stop);

    private void OnEndRepeatChecked(object sender, RoutedEventArgs e) => Choose(EndAction.Repeat);

    private void OnEndNextChecked(object sender, RoutedEventArgs e) => Choose(EndAction.PlayNext);

    private void Choose(EndAction action)
    {
        if (_applying) return;

        _settings.WhenFileEnds = action;
        _settings.Save();
    }

    private void OnEnglishChecked(object sender, RoutedEventArgs e) => Choose(AppLanguage.English);

    private void OnGermanChecked(object sender, RoutedEventArgs e) => Choose(AppLanguage.German);

    /// <summary>
    /// Switches language live. Every label bound through <c>{loc:Str}</c> — including the
    /// ones in this window and in the player behind it — re-reads itself, so the change
    /// is visible before the dialog is even closed.
    /// </summary>
    private void Choose(AppLanguage language)
    {
        if (_applying) return;

        Loc.Use(language);
    }

    private void OnIntegrationClick(object sender, RoutedEventArgs e)
    {
        // Owned by this dialog, so it comes back here when closed rather than dropping
        // the user straight out to the video.
        new AssociationDialog(this).ShowDialog();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
