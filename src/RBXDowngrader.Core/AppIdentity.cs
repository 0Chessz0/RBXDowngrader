using System.Reflection;

namespace RBXDowngrader.Core;

public static class AppIdentity
{
    public static Version Version { get; } =
        typeof(AppIdentity).Assembly.GetName().Version ?? new Version(0, 0, 0);

    public static string UserAgent => $"RBXDowngrader/{Version.ToString(3)}";
}
