using System.Linq;
using System.Windows;
using System.Windows.Media;
using Iris.Interop;
using Iris.Localization;

namespace Iris;

/// <summary>
/// Sets Iris up with Windows in one click: registers it as a handler and makes it the
/// default for every media type Windows leaves available. Types a still-installed app
/// holds are reported honestly rather than taken, because only the user may change those.
///
/// On first run this is also where the language is chosen, since it is the first thing
/// Iris ever shows.
/// </summary>
public partial class AssociationDialog : Window
{
    private enum Pane
    {
        Offer,      // not registered yet
        Registered, // registered; report what Iris opens and what it could not take
    }

    private Pane _pane;

    /// <summary>Set while the language buttons are being put into their starting state.</summary>
    private bool _applying;

    public AssociationDialog(Window owner)
    {
        InitializeComponent();
        Owner = owner;

        SourceInitialized += (_, _) =>
            Native.ApplyDarkChrome(this, (Color)FindResource("BackgroundColor"));

        _applying = true;
        EnglishOption.IsChecked = Loc.Language == AppLanguage.English;
        GermanOption.IsChecked = Loc.Language == AppLanguage.German;
        _applying = false;

        // This pane's text is assigned in code, so it has no binding to re-read.
        Loc.Changed += OnLanguageChanged;
        Closed += (_, _) => Loc.Changed -= OnLanguageChanged;

        ShowPane(FileAssociations.IsRegistered ? Pane.Registered : Pane.Offer);
    }

    private void OnLanguageChanged() => ShowPane(_pane);

    private void OnEnglishChecked(object sender, RoutedEventArgs e) => Choose(AppLanguage.English);

    private void OnGermanChecked(object sender, RoutedEventArgs e) => Choose(AppLanguage.German);

    private void Choose(AppLanguage language)
    {
        if (_applying) return;

        Loc.Use(language);
    }

    private void ShowPane(Pane pane)
    {
        _pane = pane;

        if (pane == Pane.Offer) ShowOffer();
        else ShowRegistered();
    }

    private void ShowOffer()
    {
        Heading.Text = Loc.Get(TextKey.AssocOfferHeading);
        Body.Text = Loc.Get(TextKey.AssocOfferBody, MediaTypes.VideoCount, MediaTypes.AudioCount);
        Note.Text = Loc.Get(TextKey.AssocOfferNote);

        // Offered on first run only. Reopened later, the language lives in Settings.
        LanguageRow.Visibility = Visibility.Visible;

        PathBox.Visibility = Visibility.Visible;
        PathText.Text = FileAssociations.CurrentExePath;

        PrimaryButton.Content = Loc.Get(TextKey.AssocSetUp);
        PrimaryButton.Visibility = Visibility.Visible;
        SecondaryButton.Content = Loc.Get(TextKey.AssocNotNow);
        TertiaryButton.Visibility = Visibility.Collapsed;
    }

    private void ShowRegistered()
    {
        var survey = DefaultApps.Survey();
        var mine = survey.Count(s => s.Status == DefaultApps.Status.Iris);
        var claimable = survey.Where(s => s.IsClaimable).ToList();
        var taken = survey.Where(s => s.Status == DefaultApps.Status.Taken).ToList();

        Heading.Text = Loc.Get(TextKey.AssocReadyHeading);
        LanguageRow.Visibility = Visibility.Collapsed;
        PathBox.Visibility = Visibility.Collapsed;

        Body.Text = mine == 0
            ? Loc.Get(TextKey.AssocReadyBodyNone)
            : Loc.Get(TextKey.AssocReadyBody, mine, survey.Count);

        if (claimable.Count > 0)
        {
            Note.Text = Loc.Get(
                claimable.Count == 1 ? TextKey.AssocUnassignedOne : TextKey.AssocUnassignedMany,
                Describe(claimable));

            PrimaryButton.Content = Loc.Get(TextKey.AssocMakeDefault);
            PrimaryButton.Visibility = Visibility.Visible;
        }
        else if (taken.Count > 0)
        {
            var holders = taken.Select(t => t.Holder).Where(h => !string.IsNullOrWhiteSpace(h))
                               .Distinct().ToList();

            Note.Text = Loc.Get(
                taken.Count == 1 ? TextKey.AssocTakenOne : TextKey.AssocTakenMany,
                Describe(taken),
                holders.Count == 1 ? holders[0] : Loc.Get(TextKey.AssocAnotherApp));

            PrimaryButton.Content = Loc.Get(TextKey.AssocChooseDefaults);
            PrimaryButton.Visibility = Visibility.Visible;
        }
        else
        {
            Note.Text = Loc.Get(TextKey.AssocAllSet);
            PrimaryButton.Visibility = Visibility.Collapsed;
        }

        SecondaryButton.Content = Loc.Get(TextKey.AssocClose);
        TertiaryButton.Content = Loc.Get(TextKey.AssocRemove);
        TertiaryButton.Visibility = Visibility.Visible;
    }

    /// <summary>Names a few extensions, then counts the rest, so the line never runs long.</summary>
    private static string Describe(IReadOnlyList<DefaultApps.TypeStatus> types)
    {
        const int Shown = 3;

        var listed = string.Join(", ", types.Take(Shown).Select(t => t.Type.Extension));

        return types.Count <= Shown
            ? listed
            : Loc.Get(TextKey.AssocAndMore, listed, types.Count - Shown);
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e)
    {
        if (_pane == Pane.Offer)
        {
            if (!TryRegister()) return;

            TryClaim();
            ShowPane(Pane.Registered);
            return;
        }

        // Registered: either there is something left to take, or Windows must be asked.
        if (DefaultApps.Survey().Any(s => s.IsClaimable))
        {
            TryClaim();
            ShowPane(Pane.Registered);
        }
        else
        {
            FileAssociations.OpenDefaultAppsSettings();
            Close();
        }
    }

    private void OnSecondaryClick(object sender, RoutedEventArgs e) => Close();

    private void OnTertiaryClick(object sender, RoutedEventArgs e)
    {
        try
        {
            FileAssociations.Unregister();
        }
        catch (Exception ex)
        {
            Report(Loc.Get(TextKey.AssocErrorRemove), ex);
            return;
        }

        ShowPane(Pane.Offer);
    }

    private bool TryRegister()
    {
        try
        {
            FileAssociations.Register();
            return true;
        }
        catch (Exception ex)
        {
            Report(Loc.Get(TextKey.AssocErrorRegister), ex);
            return false;
        }
    }

    private void TryClaim()
    {
        try
        {
            DefaultApps.ClaimAvailable();
        }
        catch (Exception ex)
        {
            Report(Loc.Get(TextKey.AssocErrorDefault), ex);
        }
    }

    private void Report(string summary, Exception ex) =>
        MessageBox.Show(this, $"{summary}\n\n{ex.Message}", "Iris",
            MessageBoxButton.OK, MessageBoxImage.Warning);
}
