using System.IO.Compression;
using System.Security.Cryptography;
using RBXDowngrader.Core;

namespace RBXDowngrader.Core.Tests;

public sealed class RobloxDownloadServiceTests
{
    private const string Version = "version-1111111111111111";

    [Fact]
    public async Task RetriesTransientPackageFailuresAndCompletesOnThirdAttempt()
    {
        using var temporary = new TemporaryDirectory();
        var package = CreatePlayerPackage();
        var checksum = Convert.ToHexString(MD5.HashData(package)).ToLowerInvariant();
        var manifest = CreateManifest(checksum, package.Length);
        var packageRequests = 0;
        string? userAgent = null;

        using var service = new RobloxDownloadService(new DelegateHttpHandler((request, _) =>
        {
            userAgent = request.Headers.UserAgent.ToString();
            if (request.RequestUri!.AbsolutePath.EndsWith("rbxPkgManifest.txt", StringComparison.Ordinal))
                return Task.FromResult(HttpResponses.Text(manifest));

            if (Interlocked.Increment(ref packageRequests) < 3)
                throw new HttpRequestException("temporary failure");
            return Task.FromResult(HttpResponses.Bytes(package));
        }), temporary.Path);

        var destination = await service.DownloadAsync(Version);

        Assert.Equal(3, packageRequests);
        Assert.StartsWith("RBXDowngrader/", userAgent, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(destination, "RobloxPlayerBeta.exe")));
    }

    [Fact]
    public async Task DoesNotRetryChecksumMismatch()
    {
        using var temporary = new TemporaryDirectory();
        var package = CreatePlayerPackage();
        var manifest = CreateManifest("00000000000000000000000000000000", package.Length);
        var packageRequests = 0;

        using var service = new RobloxDownloadService(new DelegateHttpHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("rbxPkgManifest.txt", StringComparison.Ordinal))
                return Task.FromResult(HttpResponses.Text(manifest));

            Interlocked.Increment(ref packageRequests);
            return Task.FromResult(HttpResponses.Bytes(package));
        }), temporary.Path);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(Version));

        Assert.Contains("Integrity check failed", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, packageRequests);
    }

    private static string CreateManifest(string checksum, int packageLength) =>
        $"v0\nRobloxApp.zip\n{checksum}\n{packageLength}\n1\n";

    private static byte[] CreatePlayerPackage()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("RobloxPlayerBeta.exe");
            using var writer = new BinaryWriter(entry.Open());
            writer.Write((byte)0);
        }
        return stream.ToArray();
    }
}
