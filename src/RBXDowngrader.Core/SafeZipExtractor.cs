using System.IO.Compression;

namespace RBXDowngrader.Core;

public static class SafeZipExtractor
{
    public static void Extract(string archivePath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        var root = Path.GetFullPath(destinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            // Official Roblox packages can prefix otherwise relative paths with a
            // slash, for example "\\abilities\\". Remove only those leading
            // separators, then apply the normal containment check below.
            var relativePath = entry.FullName.TrimStart('/', '\\');
            if (string.IsNullOrEmpty(relativePath))
            {
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unsafe path in package: {entry.FullName}");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }
}
