using System.Text.RegularExpressions;

namespace RBXDowngrader.Core;

public static partial class VersionHash
{
    [GeneratedRegex("^[a-fA-F0-9]{16}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();

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
}
