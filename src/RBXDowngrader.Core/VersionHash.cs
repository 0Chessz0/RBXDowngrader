using System.Text.RegularExpressions;

namespace RBXDowngrader.Core;

public static partial class VersionHash
{
    [GeneratedRegex("^[a-fA-F0-9]{16}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();

    [GeneratedRegex("(?<![a-fA-F0-9])(?:version-)?([a-fA-F0-9]{16})(?![a-fA-F0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmbeddedHashPattern();

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var candidate = value.Trim();
        if (candidate.StartsWith("version-", StringComparison.OrdinalIgnoreCase))
            candidate = candidate[8..];

        if (!HashPattern().IsMatch(candidate))
            return false;

        normalized = $"version-{candidate.ToLowerInvariant()}";
        return true;
    }

    public static bool TryExtract(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var match = EmbeddedHashPattern().Match(value);
        return match.Success && TryNormalize(match.Groups[1].Value, out normalized);
    }
}
