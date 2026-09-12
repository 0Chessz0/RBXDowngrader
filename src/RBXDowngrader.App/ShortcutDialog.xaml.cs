using System.Windows;
using System.Windows.Input;
using RBXDowngrader.Core;

namespace RBXDowngrader;

public partial class ShortcutDialog : Window
{
    public ShortcutDialog(InstalledVersion version, VersionShortcutState state)
    {
        InitializeComponent();
        VersionName.Text = version.DisplayName;
        DesktopShortcut.IsChecked = state.Desktop;
        StartMenuShortcut.IsChecked = state.StartMenu;
    }

    public bool CreateDesktopShortcut => DesktopShortcut.IsChecked == true;
    public bool CreateStartMenuShortcut => StartMenuShortcut.IsChecked == true;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.GetPosition(this).Y < 72)
            DragMove();
    }
}
