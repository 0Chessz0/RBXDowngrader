namespace RBXDowngrader.Core;

public sealed class UpdateSkipStore
{
    private readonly string _path;

    public UpdateSkipStore(string? path = null) => _path = path ?? AppPaths.SkippedUpdateVersion;

    public bool IsSkipped(Version version)
    {
        try
        {
            var saved = File.Exists(_path) ? File.ReadAllText(_path).Trim() : string.Empty;
            return Version.TryParse(saved, out var skipped) && version <= skipped;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public void Skip(Version version)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, version.ToString(3));
    }
}
