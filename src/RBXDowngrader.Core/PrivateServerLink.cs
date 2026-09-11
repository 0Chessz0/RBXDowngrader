namespace RBXDowngrader.Core;

public static class PrivateServerLink
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var candidate = value.Trim();
        if (candidate.StartsWith("roblox://", StringComparison.OrdinalIgnoreCase))
        {
            if (candidate.Length <= "roblox://".Length ||
                candidate.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
            {
                return false;
            }

            normalized = candidate;
            return true;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var isRobloxWebLink = (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
            (uri.Host.Equals("roblox.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".roblox.com", StringComparison.OrdinalIgnoreCase));
        if (!isRobloxWebLink)
            return false;

        normalized = uri.AbsoluteUri;
        return true;
    }
}
