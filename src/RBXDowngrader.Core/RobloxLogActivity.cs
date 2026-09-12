using System.Globalization;
using System.Text.RegularExpressions;

namespace RBXDowngrader.Core;

public enum RobloxActivityKind
{
    Joined,
    Left
}

public sealed record RobloxActivity(RobloxActivityKind Kind, long? PlaceId = null);

public static partial class RobloxLogActivity
{
    public static RobloxActivity? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var match = JoinLineRegex().Match(line);
        if (!match.Success)
            match = JoinMetricsRegex().Match(line);
        if (match.Success
            && long.TryParse(match.Groups["placeId"].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var placeId)
            && placeId > 0)
        {
            return new RobloxActivity(RobloxActivityKind.Joined, placeId);
        }

        return LeaveLineRegex().IsMatch(line)
            ? new RobloxActivity(RobloxActivityKind.Left)
            : null;
    }

    [GeneratedRegex(@"\[FLog::Output\].*!\s+Joining game\s+'.+?'\s+place\s+(?<placeId>\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JoinLineRegex();

    [GeneratedRegex(@"\[FLog::GameJoinLoadTime\].*\bplaceid\s*:\s*(?<placeId>\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JoinMetricsRegex();

    [GeneratedRegex(@"(?:Disconnected from Game|leaveUGCGameInternal)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeaveLineRegex();
}
