using System.IO;
using System.Reflection;
using System.Windows;

namespace RBXDowngrader.Installer;

public partial class App : Application
{
    private const string ExtractPayloadArgument = "--extract-payload";

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        if (e.Args.Length == 2 && e.Args[0].Equals(ExtractPayloadArgument, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var destination = Path.GetFullPath(e.Args[1]);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var source = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("RBXDowngrader.Payload.zip")
                    ?? throw new InvalidDataException("Installer payload is missing.");
                using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.Read);
                source.CopyTo(target);
                Shutdown(0);
            }
            catch
            {
                Shutdown(1);
            }

            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
