namespace RBXDowngrader.Core;

public static class VersionLaunchRequest
{
    public const string Argument = "--launch-version";

    public static bool TryParse(IReadOnlyList<string> arguments, out string version)
    {
        version = string.Empty;
        return arguments.Count == 2
            && arguments[0].Equals(Argument, StringComparison.OrdinalIgnoreCase)
            && VersionHash.TryNormalize(arguments[1], out version);
    }

    public static string CreateShortcutArguments(string version)
    {
        if (!VersionHash.TryNormalize(version, out var normalized))
            throw new ArgumentException("The version hash is invalid.", nameof(version));

        return $"{Argument} {normalized}";
    }

    public static bool MatchesShortcutArguments(string? arguments, string version) =>
        VersionHash.TryNormalize(version, out var normalized)
        && string.Equals(
            arguments?.Trim(),
            $"{Argument} {normalized}",
            StringComparison.OrdinalIgnoreCase);
}
