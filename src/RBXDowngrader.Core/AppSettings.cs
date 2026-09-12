namespace RBXDowngrader.Core;

public sealed record AppSettings
{
    public bool DiscordRichPresenceEnabled { get; init; } = true;
    public bool MinimizeToTrayOnLaunch { get; init; } = true;
    public bool AutomaticUpdateChecksEnabled { get; init; } = true;
}
