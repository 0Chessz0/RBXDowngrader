namespace RBXDowngrader.Core;

public static class ApplicationDataPathRules
{
    public static bool IsProtectedPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        var normalized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0
            && (segments[0].Equals("robloxversions", StringComparison.OrdinalIgnoreCase)
                || segments[0].Equals("temp", StringComparison.OrdinalIgnoreCase)
                || IsProtectedRootFile(normalized));
    }

    public static bool IsAllowedExistingEntry(string relativePath, bool isDirectory)
    {
        if (isDirectory)
        {
            var name = Path.GetFileName(relativePath);
            return name.Equals("robloxversions", StringComparison.OrdinalIgnoreCase)
                || name.Equals("temp", StringComparison.OrdinalIgnoreCase);
        }

        return IsProtectedRootFile(relativePath);
    }

    private static bool IsProtectedRootFile(string relativePath) =>
        relativePath.Equals("RBXDowngrader.log", StringComparison.OrdinalIgnoreCase)
        || relativePath.Equals("recent-builds-cache.json", StringComparison.OrdinalIgnoreCase)
        || relativePath.Equals("update-checks.json", StringComparison.OrdinalIgnoreCase)
        || relativePath.Equals("skipped-update-version.txt", StringComparison.OrdinalIgnoreCase);
}
