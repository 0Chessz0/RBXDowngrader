using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RBXDowngrader.Core;

namespace RBXDowngrader;

public partial class PrivateServerDialog : Window
{
    public string PrivateServerUrl { get; private set; } = string.Empty;

    public PrivateServerDialog() => InitializeComponent();

    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (!PrivateServerLink.TryNormalize(LinkInput.Text, out var normalized))
        {
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        PrivateServerUrl = normalized;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void LinkInput_TextChanged(object sender, TextChangedEventArgs e) =>
        ValidationText.Visibility = Visibility.Collapsed;

    private void LinkInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        Launch_Click(sender, new RoutedEventArgs());
        e.Handled = true;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => LinkInput.Focus();

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.GetPosition(this).Y < 58)
            DragMove();
    }
}
