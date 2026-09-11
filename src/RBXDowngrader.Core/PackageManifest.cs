namespace RBXDowngrader.Core;

public static class PackageManifest
{
    public static IReadOnlyList<PackageManifestEntry> Parse(string content)
    {
        var lines = content
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var offset = lines.Length % 4 == 1 ? 1 : 0;
        if (lines.Length - offset <= 0 || (lines.Length - offset) % 4 != 0)
            throw new FormatException("The deployment manifest has an unexpected format.");

        var packages = new List<PackageManifestEntry>();
        for (var i = offset; i < lines.Length; i += 4)
        {
            var packageName = lines[i];
            if (!packageName.Equals(Path.GetFileName(packageName), StringComparison.Ordinal) ||
                packageName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                (!packageName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                 !packageName.Equals("RobloxPlayerInstaller.exe", StringComparison.OrdinalIgnoreCase)))
            {
                throw new FormatException("The deployment manifest contains an unsafe package name.");
            }

            var firstValue = lines[i + 1];
            var secondValue = lines[i + 2];
            var thirdValue = lines[i + 3];
            string checksum;
            long compressedSize;
            long uncompressedSize;

            // Current manifests place the checksum before both sizes. Older manifests
            // used by third-party deployment mirrors may put the checksum last.
            if (IsChecksum(firstValue) &&
                long.TryParse(secondValue, out compressedSize) &&
                long.TryParse(thirdValue, out uncompressedSize))
            {
                checksum = firstValue;
            }
            else if (long.TryParse(firstValue, out compressedSize) &&
                     long.TryParse(secondValue, out uncompressedSize) &&
                     IsChecksum(thirdValue))
            {
                checksum = thirdValue;
            }
            else
            {
                throw new FormatException($"Invalid sizes for package '{packageName}'.");
            }

            if (compressedSize < 0 || uncompressedSize < 0)
                throw new FormatException($"Invalid sizes for package '{packageName}'.");

            packages.Add(new PackageManifestEntry(
                packageName, compressedSize, uncompressedSize, checksum));
        }

        return packages;
    }

    private static bool IsChecksum(string value) =>
        value.Length == 32 && value.All(Uri.IsHexDigit);
}
