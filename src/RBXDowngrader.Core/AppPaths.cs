namespace RBXDowngrader.Core;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RBXDowngrader");

    public static string Versions { get; } = Path.Combine(Root, "robloxversions");
    public static string Temp { get; } = Path.Combine(Root, "temp");
    public static string LogFile { get; } = Path.Combine(Root, "RBXDowngrader.log");
    public static string RecentBuildsCache { get; } = Path.Combine(Root, "recent-builds-cache.json");
    public static string UpdateCheckLog { get; } = Path.Combine(Root, "update-checks.json");
    public static string SkippedUpdateVersion { get; } = Path.Combine(Root, "skipped-update-version.txt");
    public static string Settings { get; } = Path.Combine(Root, "settings.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Versions);
        Directory.CreateDirectory(Temp);
    }
}
