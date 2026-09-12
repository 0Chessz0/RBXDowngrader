using System.Windows;
using System.Windows.Input;
using RBXDowngrader.Core;

namespace RBXDowngrader;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _settingsStore;
    private bool _isInitializing = true;

    public SettingsWindow(SettingsStore settingsStore, AppSettings settings)
    {
        InitializeComponent();
        _settingsStore = settingsStore;
        Settings = settings;
        RichPresenceToggle.IsChecked = settings.DiscordRichPresenceEnabled;
        MinimizeToTrayToggle.IsChecked = settings.MinimizeToTrayOnLaunch;
        AutomaticUpdatesToggle.IsChecked = settings.AutomaticUpdateChecksEnabled;
        _isInitializing = false;
    }

    public AppSettings Settings { get; private set; }
    public event Action<AppSettings>? SettingsChanged;

    private void Save(AppSettings settings)
    {
        _settingsStore.Save(settings);
        Settings = settings;
        SettingsChanged?.Invoke(settings);
    }

    private void RichPresenceToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
            return;

        Save(Settings with { DiscordRichPresenceEnabled = RichPresenceToggle.IsChecked == true });
    }

    private void MinimizeToTrayToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
            return;

        Save(Settings with { MinimizeToTrayOnLaunch = MinimizeToTrayToggle.IsChecked == true });
    }

    private void AutomaticUpdatesToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
            return;

        Save(Settings with { AutomaticUpdateChecksEnabled = AutomaticUpdatesToggle.IsChecked == true });
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.GetPosition(this).Y < 72)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
