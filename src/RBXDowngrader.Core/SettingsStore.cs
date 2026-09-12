using System.Text.Json;

namespace RBXDowngrader.Core;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();
    private readonly string _path;

    public SettingsStore(string? path = null)
    {
        _path = path ?? AppPaths.Settings;
    }

    public AppSettings Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_path))
                return new AppSettings();

            try
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            catch (JsonException)
            {
                return new AppSettings();
            }
            catch (IOException)
            {
                return new AppSettings();
            }
            catch (UnauthorizedAccessException)
            {
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_sync)
        {
            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("The settings path has no parent directory.");
            Directory.CreateDirectory(directory);
            var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";

            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
                File.Move(temporaryPath, _path, overwrite: true);
            }
            finally
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
