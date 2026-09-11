namespace RBXDowngrader.Core;

public sealed record InstalledVersion(
    string Version,
    string DirectoryPath,
    DateTimeOffset InstalledAt,
    long SizeBytes,
    string ExecutablePath)
{
    public string ShortHash => Version.StartsWith("version-", StringComparison.OrdinalIgnoreCase)
        ? Version[8..]
        : Version;

    public string SizeText => SizeBytes switch
    {
        >= 1_073_741_824 => $"{SizeBytes / 1_073_741_824d:0.0} GB",
        >= 1_048_576 => $"{SizeBytes / 1_048_576d:0} MB",
        >= 1024 => $"{SizeBytes / 1024d:0} KB",
        _ => $"{SizeBytes} B"
    };
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
