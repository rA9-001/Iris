using System.Windows;

// Iris ships no per-theme dictionaries; everything lives in Theme/Styles.xaml, which
// App.xaml merges. WPF still wants to be told where to look.
[assembly: ThemeInfo(
    themeDictionaryLocation: ResourceDictionaryLocation.None,
    genericDictionaryLocation: ResourceDictionaryLocation.SourceAssembly)]
