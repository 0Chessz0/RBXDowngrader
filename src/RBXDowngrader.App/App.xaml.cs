using System.Windows;
using RBXDowngrader.Core;

namespace RBXDowngrader;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\RBXDowngrader.SingleInstance";
    private const string ActivationEventName = @"Local\RBXDowngrader.Activate";
    private readonly CancellationTokenSource _activationCancellation = new();
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationEvent;
    private Task? _activationListener;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length == 5 && string.Equals(
                e.Args[0], UpdateWorker.ApplyArgument, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(e.Args[4], out var oldProcessId))
        {
            try
            {
                await UpdateWorker.ApplyAsync(e.Args[1], e.Args[2], e.Args[3], oldProcessId);
            }
            catch
            {
                // ApplyAsync restores and relaunches the previous copy on a failed swap.
            }
            Shutdown();
            return;
        }

        if (e.Args.Length == 2 && string.Equals(
                e.Args[0], VersionStore.DeleteWorkerArgument, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await VersionStore.RunDeleteWorkerAsync(e.Args[1]);
            }
            catch
            {
                // The worker is intentionally silent. Pending folders are retried next launch.
            }
            Shutdown();
            return;
        }

        if (!TryBecomePrimaryInstance())
        {
            SignalPrimaryInstance();
            Shutdown();
            return;
        }

        try { VersionStore.ResumePendingDeletes(); }
        catch { }

        var cleanupArguments = e.Args.Length == 3
            && string.Equals(e.Args[0], UpdateWorker.CleanupArgument, StringComparison.OrdinalIgnoreCase)
                ? (UpdateRoot: e.Args[1], BackupRoot: e.Args[2])
                : ((string UpdateRoot, string BackupRoot)?)null;
        var updateFailed = e.Args.Contains(UpdateWorker.FailedArgument, StringComparer.OrdinalIgnoreCase);

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        try
        {
            var window = new MainWindow(updateFailed);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            var cause = ex;
            while (cause.InnerException is not null)
                cause = cause.InnerException;

            MessageBox.Show(
                $"RBXDowngrader could not start.\n\n{cause.Message}",
                "RBXDowngrader",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _activationListener = ListenForActivationAsync(_activationCancellation.Token);
        if (cleanupArguments is { } cleanup)
            _ = CleanupUpdateAsync(cleanup.UpdateRoot, cleanup.BackupRoot);
    }

    private static async Task CleanupUpdateAsync(string updateRoot, string backupRoot)
    {
        try { await UpdateWorker.CleanupAsync(updateRoot, backupRoot); }
        catch { }
    }

    private bool TryBecomePrimaryInstance()
    {
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            return false;
        }

        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ActivationEventName);
        return true;
    }

    private static void SignalPrimaryInstance()
    {
        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
            activationEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task ListenForActivationAsync(CancellationToken cancellationToken)
    {
        if (_activationEvent is null)
            return;

        await Task.Run(() =>
        {
            var handles = new WaitHandle[] { _activationEvent, cancellationToken.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                Dispatcher.Invoke(() =>
                {
                    if (MainWindow is null)
                        return;

                    if (MainWindow is MainWindow mainWindow)
                    {
                        mainWindow.RestoreFromTray();
                        return;
                    }

                    if (MainWindow.WindowState == WindowState.Minimized)
                        MainWindow.WindowState = WindowState.Normal;
                    MainWindow.Show();
                    MainWindow.Activate();
                });
            }
        }, CancellationToken.None);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationCancellation.Cancel();
        _activationEvent?.Set();
        try { _activationListener?.Wait(TimeSpan.FromSeconds(1)); }
        catch (AggregateException) { }
        _activationEvent?.Dispose();
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        _activationCancellation.Dispose();
        base.OnExit(e);
    }
}
