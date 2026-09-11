using System.Globalization;

namespace RBXDowngrader.Core;

public sealed record InstalledVersion(
    string Version,
    string DirectoryPath,
    DateTimeOffset InstalledAt,
    long SizeBytes,
    string ExecutablePath,
    string? CustomName = null)
{
    public string ShortHash => Version.StartsWith("version-", StringComparison.OrdinalIgnoreCase)
        ? Version[8..]
        : Version;

    public string DisplayName => string.IsNullOrWhiteSpace(CustomName) ? ShortHash : CustomName;
    public string SizeText => FileSizeFormatter.Format(SizeBytes);
    public string DetailsText => string.IsNullOrWhiteSpace(CustomName)
        ? $"{SizeText}  •  {InstalledAt:dd MMM yyyy}"
        : $"{ShortHash}  •  {SizeText}  •  {InstalledAt:dd MMM yyyy}";
}

public static class FileSizeFormatter
{
    public static string Format(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{(bytes / 1_073_741_824d).ToString("0.0", CultureInfo.InvariantCulture)} GB",
        >= 1_048_576 => $"{(bytes / 1_048_576d).ToString("0", CultureInfo.InvariantCulture)} MB",
        >= 1024 => $"{(bytes / 1024d).ToString("0", CultureInfo.InvariantCulture)} KB",
        _ => $"{bytes.ToString(CultureInfo.InvariantCulture)} B"
    };
}

public sealed record RecentBuild(
    string Version,
    string DisplayVersion,
    DateTimeOffset? ReleasedAt,
    bool IsCurrent)
{
    public string ShortHash => Version.StartsWith("version-", StringComparison.OrdinalIgnoreCase)
        ? Version[8..]
        : Version;

    public string StatusText => IsCurrent ? "CURRENT" : string.Empty;
}

public sealed record DownloadProgress(
    string Status,
    double Percentage,
    long BytesReceived = 0,
    long TotalBytes = 0);

public sealed record PackageManifestEntry(
    string Name,
    long CompressedSize,
    long UncompressedSize,
    string Checksum);
