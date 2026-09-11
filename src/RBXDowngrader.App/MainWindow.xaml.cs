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
    private readonly RecentBuildService _recentBuildService = new();
    private CancellationTokenSource? _downloadCancellation;
    private bool _isBusy;
    private bool _isLoadingRecentBuilds;

    public ObservableCollection<InstalledVersion> Versions { get; } = [];
    public ObservableCollection<RecentBuild> RecentBuilds { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureCreated();
        await RefreshVersionsAsync();
        TryPrefillVersionFromClipboard();
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

            UpdateInstalledSummary();
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

    private void Delete_Click(object sender, RoutedEventArgs e)
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
            _store.QueueDelete(version);
            Versions.Remove(version);
            UpdateInstalledSummary();
        }
        catch (Exception ex)
        {
            SetStatus(ToFriendlyMessage(ex), isError: true);
        }
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || (sender as FrameworkElement)?.DataContext is not InstalledVersion version)
            return;

        var dialog = new RenameDialog(version.CustomName ?? version.ShortHash) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var renamed = await _store.RenameAsync(version, dialog.ClientName);
            var index = Versions.IndexOf(version);
            if (index >= 0)
                Versions[index] = renamed;
        }
        catch (Exception ex)
        {
            SetStatus(ToFriendlyMessage(ex), isError: true);
        }
    }

    private async void RecentBuilds_Click(object sender, RoutedEventArgs e)
    {
        RecentBuildsPopup.IsOpen = !RecentBuildsPopup.IsOpen;
        if (!RecentBuildsPopup.IsOpen || _isLoadingRecentBuilds)
            return;

        _isLoadingRecentBuilds = true;
        RecentBuilds.Clear();
        RecentBuildsList.Visibility = Visibility.Collapsed;
        RecentBuildsState.Text = "Loading";
        RecentBuildsState.Visibility = Visibility.Visible;

        try
        {
            var builds = await _recentBuildService.GetLatestAsync();
            foreach (var build in builds)
                RecentBuilds.Add(build);

            if (RecentBuilds.Count == 0)
            {
                RecentBuildsState.Text = "No recent builds found";
            }
            else
            {
                RecentBuildsState.Visibility = Visibility.Collapsed;
                RecentBuildsList.Visibility = Visibility.Visible;
            }
        }
        catch
        {
            RecentBuildsState.Text = "Recent builds unavailable";
        }
        finally
        {
            _isLoadingRecentBuilds = false;
        }
    }

    private void RecentBuild_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RecentBuild build)
            return;

        VersionInput.Text = build.Version;
        VersionInput.CaretIndex = VersionInput.Text.Length;
        RecentBuildsPopup.IsOpen = false;
        VersionInput.Focus();
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

    private void VersionInput_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText))
            return;

        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text
            || !VersionHash.TryExtract(text, out var version))
            return;

        VersionInput.Text = version;
        VersionInput.CaretIndex = VersionInput.Text.Length;
        e.CancelCommand();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _downloadCancellation?.Cancel();

    private void SetBusyState(bool busy)
    {
        VersionInput.IsEnabled = !busy;
        RecentBuildsButton.IsEnabled = !busy;
        DownloadButton.IsEnabled = !busy && VersionHash.TryNormalize(VersionInput.Text, out _);
        DownloadButton.Content = busy ? "Working" : "Download";
        DownloadProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (!busy)
            DownloadProgress.Value = 0;
    }

    private void TryPrefillVersionFromClipboard()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(VersionInput.Text)
                && Clipboard.ContainsText()
                && VersionHash.TryExtract(Clipboard.GetText(), out var version))
            {
                VersionInput.Text = version;
                VersionInput.CaretIndex = VersionInput.Text.Length;
            }
        }
        catch
        {
            // Clipboard access can fail briefly while another app owns it.
        }
    }

    private void UpdateInstalledSummary()
    {
        var count = Versions.Count;
        var totalSize = Versions.Sum(version => version.SizeBytes);
        VersionCount.Text = $"{FileSizeFormatter.Format(totalSize)} used across {count} {(count == 1 ? "build" : "builds")}";
        EmptyState.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        VersionsList.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
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
        _recentBuildService.Dispose();
        base.OnClosed(e);
    }
}
