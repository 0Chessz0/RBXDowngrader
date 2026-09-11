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

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Versions);
        Directory.CreateDirectory(Temp);
    }
}
