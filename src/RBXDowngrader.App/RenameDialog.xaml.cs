using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RBXDowngrader;

public partial class RenameDialog : Window
{
    public string ClientName { get; private set; } = string.Empty;

    public RenameDialog(string currentName)
    {
        InitializeComponent();
        NameInput.Text = currentName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameInput.Text.Trim();
        if (name.Length is < 1 or > 40 || name.Any(char.IsControl))
        {
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        ClientName = name;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void NameInput_TextChanged(object sender, TextChangedEventArgs e) =>
        ValidationText.Visibility = Visibility.Collapsed;

    private void NameInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        Save_Click(sender, new RoutedEventArgs());
        e.Handled = true;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        NameInput.Focus();
        NameInput.SelectAll();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.GetPosition(this).Y < 58)
            DragMove();
    }
}
