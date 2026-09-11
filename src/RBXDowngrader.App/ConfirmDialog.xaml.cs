using System.Windows;

namespace RBXDowngrader;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string heading, string message)
    {
        InitializeComponent();
        Heading.Text = heading;
        Message.Text = message;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
