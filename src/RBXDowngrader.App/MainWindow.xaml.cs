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
    private readonly UpdateService _updateService = new();
    private readonly UpdateCheckThrottle _updateCheckThrottle = new();
    private readonly UpdateSkipStore _updateSkipStore = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly DiscordPresenceService _discordPresence = new();
    private readonly TrayIconService _trayIcon;
    private CancellationTokenSource? _downloadCancellation;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _isBusy;
    private bool _isLoadingRecentBuilds;
    private bool _isCheckingForUpdates;
    private bool _isUpdating;
    private readonly bool _showUpdateFailure;
    private UpdateRelease? _availableUpdate;
    private AppSettings _settings = new();

    public ObservableCollection<InstalledVersion> Versions { get; } = [];
    public ObservableCollection<RecentBuild> RecentBuilds { get; } = [];

    public MainWindow(bool showUpdateFailure = false)
    {
        InitializeComponent();
        DataContext = this;
        _showUpdateFailure = showUpdateFailure;
        _settings = _settingsStore.Load();
        _trayIcon = new TrayIconService(this);
        VersionLabel.Text = $"v{AppIdentity.Version.ToString(3)}";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureCreated();
        await RefreshVersionsAsync();
        TryPrefillVersionFromClipboard();
        VersionInput.Focus();
        if (_settings.DiscordRichPresenceEnabled)
            _discordPresence.Start();
        if (_showUpdateFailure)
            ShowUpdateFailure();
        else if (_settings.AutomaticUpdateChecksEnabled)
            _ = CheckForUpdatesAsync(manual: false);
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, ".install-scope")))
        {
            if (manual)
                SetStatus("Updates are available for installed copies", isError: true);
            return;
        }

        if (_isCheckingForUpdates)
            return;

        if (!_updateCheckThrottle.TryAcquire(out var retryAfter))
        {
            if (manual)
                SetStatus($"Try again in {FormatRetryAfter(retryAfter)}", isError: true);
            return;
        }

        _isCheckingForUpdates = true;
        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesButton.Content = "...";
        try
        {
            _availableUpdate = await _updateService.CheckAsync(_lifetimeCancellation.Token);
            if (_availableUpdate is null)
            {
                ResetReleaseNotes();
                UpdateBanner.Visibility = Visibility.Collapsed;
                if (manual)
                    SetStatus("You're up to date");
                return;
            }

            if (!manual && _updateSkipStore.IsSkipped(_availableUpdate.AvailableVersion))
            {
                ResetReleaseNotes();
                UpdateBanner.Visibility = Visibility.Collapsed;
                return;
            }

            UpdateBannerText.Text = $"RBXDowngrader {_availableUpdate.AvailableVersion.ToString(3)} is available";
            PopulateReleaseNotes(_availableUpdate.ReleaseNotes);
            UpdateButton.Content = "Update";
            UpdateButton.IsEnabled = true;
            SkipUpdateButton.Visibility = Visibility.Visible;
            UpdateBanner.Visibility = Visibility.Visible;
            if (manual)
                SetStatus("Update available");
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (manual)
                SetStatus("Update check unavailable", isError: true);
        }
        finally
        {
            _isCheckingForUpdates = false;
            CheckUpdatesButton.Content = "Check";
            CheckUpdatesButton.IsEnabled = !_isBusy;
        }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) =>
        await CheckForUpdatesAsync(manual: true);

    private void WhatsNew_Click(object sender, RoutedEventArgs e)
    {
        var showNotes = ReleaseNotesPanel.Visibility != Visibility.Visible;
        ReleaseNotesPanel.Visibility = showNotes ? Visibility.Visible : Visibility.Collapsed;
        WhatsNewButton.Content = showNotes ? "Hide notes" : "What's new";
    }

    private void SkipUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null)
            return;

        try
        {
            _updateSkipStore.Skip(_availableUpdate.AvailableVersion);
            _availableUpdate = null;
            ResetReleaseNotes();
            UpdateBanner.Visibility = Visibility.Collapsed;
            SetStatus("Update skipped");
        }
        catch (Exception ex)
        {
            SetStatus(ToFriendlyMessage(ex), isError: true);
        }
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null || _isUpdating)
            return;

        _isUpdating = true;
        UpdateButton.IsEnabled = false;
        UpdateBannerText.Text = "Downloading update";
        var progress = new Progress<double>(percentage =>
            UpdateBannerText.Text = $"Downloading update  {percentage:0}%");

        try
        {
            var prepared = await _updateService.PrepareAsync(
                _availableUpdate,
                progress,
                _lifetimeCancellation.Token);
            UpdateBannerText.Text = "Restarting";
            _updateService.StartWorker(prepared, AppContext.BaseDirectory, Environment.ProcessId);
            ResetReleaseNotes();
            UpdateBanner.Visibility = Visibility.Collapsed;
            Close();
        }
        catch (OperationCanceledException) { }
        catch
        {
            UpdateBannerText.Text = "Update could not be downloaded";
            UpdateButton.Content = "Retry";
            UpdateButton.IsEnabled = true;
            _isUpdating = false;
        }
    }

    private void ShowUpdateFailure()
    {
        ResetReleaseNotes();
        UpdateBannerText.Text = "Update could not be installed";
        UpdateButton.Visibility = Visibility.Collapsed;
        SkipUpdateButton.Visibility = Visibility.Collapsed;
        UpdateBanner.Visibility = Visibility.Visible;
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
            if (_settings.MinimizeToTrayOnLaunch)
                _trayIcon.HideToTray();
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
            if (_settings.MinimizeToTrayOnLaunch)
                _trayIcon.HideToTray();
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

    private void Shortcuts_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || (sender as FrameworkElement)?.DataContext is not InstalledVersion version)
            return;

        try
        {
            var dialog = new ShortcutDialog(version, _store.GetShortcutState(version)) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            _store.SetShortcutState(
                version,
                dialog.CreateDesktopShortcut,
                dialog.CreateStartMenuShortcut);
            SetStatus("Shortcuts updated");
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
        CachedResultsText.Visibility = Visibility.Collapsed;
        RecentBuildsState.Text = "Loading";
        RecentBuildsState.Visibility = Visibility.Visible;

        try
        {
            var result = await _recentBuildService.GetLatestAsync();
            foreach (var build in result.Builds)
                RecentBuilds.Add(build);

            if (RecentBuilds.Count == 0)
            {
                RecentBuildsState.Text = "No recent builds found";
            }
            else
            {
                RecentBuildsState.Visibility = Visibility.Collapsed;
                CachedResultsText.Visibility = result.IsCached ? Visibility.Visible : Visibility.Collapsed;
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
        CheckUpdatesButton.IsEnabled = !busy && !_isCheckingForUpdates;
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

    private static string FormatRetryAfter(TimeSpan retryAfter) => retryAfter.TotalMinutes >= 1
        ? $"{Math.Ceiling(retryAfter.TotalMinutes):0} minutes"
        : $"{Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds)):0} seconds";

    private static string FormatReleaseNotes(string releaseNotes)
    {
        const int maximumLength = 480;
        if (string.IsNullOrWhiteSpace(releaseNotes))
            return string.Empty;

        var lines = releaseNotes
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var compactedLines = new List<string>(lines.Length);
        var previousLineWasBlank = false;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.Length == 0)
            {
                if (compactedLines.Count > 0 && !previousLineWasBlank)
                    compactedLines.Add(string.Empty);
                previousLineWasBlank = true;
                continue;
            }

            compactedLines.Add(trimmedLine);
            previousLineWasBlank = false;
        }

        while (compactedLines.Count > 0 && compactedLines[^1].Length == 0)
            compactedLines.RemoveAt(compactedLines.Count - 1);

        var normalized = string.Join(Environment.NewLine, compactedLines);
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..(maximumLength - 3)].TrimEnd() + "...";
    }

    private void PopulateReleaseNotes(string releaseNotes)
    {
        ResetReleaseNotes();
        var excerpt = FormatReleaseNotes(releaseNotes);
        if (excerpt.Length == 0)
            return;

        ReleaseNotesText.Text = excerpt;
        WhatsNewButton.Visibility = Visibility.Visible;
    }

    private void ResetReleaseNotes()
    {
        ReleaseNotesPanel.Visibility = Visibility.Collapsed;
        ReleaseNotesText.Text = string.Empty;
        WhatsNewButton.Content = "What's new";
        WhatsNewButton.Visibility = Visibility.Collapsed;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.GetPosition(this).Y < 76)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    public void RestoreFromTray() => _trayIcon.Restore();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_settingsStore, _settings) { Owner = this };
        settingsWindow.SettingsChanged += settings =>
        {
            _settings = settings;
            if (settings.DiscordRichPresenceEnabled)
                _discordPresence.Start();
            else
                _ = _discordPresence.StopAsync();
        };
        settingsWindow.ShowDialog();
        _settings = settingsWindow.Settings;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _downloadCancellation?.Cancel();
        _lifetimeCancellation.Cancel();
        _downloader.Dispose();
        _recentBuildService.Dispose();
        _updateService.Dispose();
        _discordPresence.Dispose();
        _trayIcon.Dispose();
        _lifetimeCancellation.Dispose();
        base.OnClosed(e);
    }
}
