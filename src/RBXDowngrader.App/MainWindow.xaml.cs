using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RBXDowngrader.Core;

namespace RBXDowngrader;

public partial class MainWindow : Window
{
    private readonly VersionStore _store = new();
    private readonly RobloxDownloadService _downloader = new();
    private CancellationTokenSource? _downloadCancellation;
    private bool _isBusy;

    public ObservableCollection<InstalledVersion> Versions { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureCreated();
        await RefreshVersionsAsync();
        VersionInput.Focus();
    }

    private async Task RefreshVersionsAsync()
    {
        try
        {
            var installed = await _store.GetInstalledAsync();
            Versions.Clear();
            foreach (var version in installed)
                Versions.Add(version);

            VersionCount.Text = Versions.Count == 1 ? "1 version" : $"{Versions.Count} versions";
            EmptyState.Visibility = Versions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            VersionsList.Visibility = Versions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || !VersionHash.TryNormalize(VersionInput.Text, out var version))
            return;

        _isBusy = true;
        _downloadCancellation = new CancellationTokenSource();
        SetBusyState(true);

        var progress = new Progress<DownloadProgress>(update =>
        {
            DownloadProgress.Value = update.Percentage;
            StatusText.Text = update.Status;
        });

        try
        {
            await _downloader.DownloadAsync(version, progress, _downloadCancellation.Token);
            VersionInput.Clear();
            SetStatus("Download complete");
            await RefreshVersionsAsync();
        }
        catch (OperationCanceledException)
        {
            SetStatus("Download canceled");
        }
        catch (Exception ex)
        {
            SetStatus(ToFriendlyMessage(ex), isError: true);
        }
        finally
        {
            _downloadCancellation.Dispose();
            _downloadCancellation = null;
            _isBusy = false;
            SetBusyState(false);
        }
    }

    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not InstalledVersion version)
            return;

        try
        {
            _store.Launch(version);
            SetStatus($"Launched {version.ShortHash}");
        }
        catch (Exception ex)
        {
            SetStatus(ToFriendlyMessage(ex), isError: true);
        }
    }

    private void PrivateServer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not InstalledVersion version)
            return;

        var dialog = new PrivateServerDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            _store.LaunchPrivateServer(version, dialog.PrivateServerUrl);
            SetStatus($"Launched private server with {version.ShortHash}");
        }
        catch (Exception ex)
        {
            SetStatus(ToFriendlyMessage(ex), isError: true);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || (sender as FrameworkElement)?.DataContext is not InstalledVersion version)
            return;

        var dialog = new ConfirmDialog(
            "Delete version?",
            $"{version.ShortHash} and its {version.SizeText} of files will be removed.")
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            SetStatus("Deleting version");
            await _store.DeleteAsync(version);
            await RefreshVersionsAsync();
            SetStatus("Version deleted");
        }
        catch (Exception ex)
        {
            SetStatus(ToFriendlyMessage(ex), isError: true);
        }
    }

    private void VersionInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        DownloadButton.IsEnabled = !_isBusy && VersionHash.TryNormalize(VersionInput.Text, out _);
    }

    private void VersionInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DownloadButton.IsEnabled)
        {
            Download_Click(DownloadButton, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _downloadCancellation?.Cancel();

    private void SetBusyState(bool busy)
    {
        VersionInput.IsEnabled = !busy;
        DownloadButton.IsEnabled = !busy && VersionHash.TryNormalize(VersionInput.Text, out _);
        DownloadButton.Content = busy ? "Working" : "Download";
        DownloadProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (!busy)
            DownloadProgress.Value = 0;
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Foreground = isError
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 111, 111))
            : (System.Windows.Media.Brush)FindResource("MutedBrush");
    }

    private static string ToFriendlyMessage(Exception exception) => exception switch
    {
        HttpRequestException => "Could not reach the Roblox deployment server",
        InvalidDataException => exception.Message,
        IOException => "A file is in use or storage is unavailable",
        _ => exception.Message
    };

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.GetPosition(this).Y < 76)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _downloadCancellation?.Cancel();
        _downloader.Dispose();
        base.OnClosed(e);
    }
}
