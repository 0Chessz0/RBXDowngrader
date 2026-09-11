using System.Windows;
using RBXDowngrader.Core;

namespace RBXDowngrader;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        try { VersionStore.ResumePendingDeletes(); }
        catch { }

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
