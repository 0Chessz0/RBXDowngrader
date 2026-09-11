using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RBXDowngrader.Core;

namespace RBXDowngrader.Core.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task FindsAndVerifiesANewerReleasePayload()
    {
        using var temporary = new TemporaryDirectory();
        var package = CreateUpdatePackage();
        var hash = Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant();
        var assetName = $"RBXDowngraderUpdate-{RuntimeIdentifier()}.zip";
        var releaseJson = $$"""
            {"tag_name":"v9.0.0","assets":[{"name":"{{assetName}}","browser_download_url":"https://example.com/update.zip","size":{{package.Length}},"digest":"sha256:{{hash}}"}]}
            """;
        using var client = new HttpClient(new DelegateHttpHandler((request, _) =>
            Task.FromResult(request.RequestUri!.Host == "api.github.com"
                ? HttpResponses.Json(releaseJson)
                : HttpResponses.Bytes(package))));
        using var service = new UpdateService(client, temporary.Path, new Version(1, 0, 0));

        var release = await service.CheckAsync();
        var prepared = await service.PrepareAsync(Assert.IsType<UpdateRelease>(release));

        Assert.Equal(new Version(9, 0, 0), prepared.Release.AvailableVersion);
        Assert.True(File.Exists(Path.Combine(prepared.PayloadDirectory, "RBXDowngrader.exe")));
        Assert.True(File.Exists(Path.Combine(prepared.PayloadDirectory, "uninstall.exe")));
    }

    [Fact]
    public async Task RejectsAnUpdateWithTheWrongSha256()
    {
        using var temporary = new TemporaryDirectory();
        var package = CreateUpdatePackage();
        using var client = new HttpClient(new DelegateHttpHandler((_, _) =>
            Task.FromResult(HttpResponses.Bytes(package))));
        using var service = new UpdateService(client, temporary.Path, new Version(1, 0, 0));
        var release = new UpdateRelease(
            new Version(9, 0, 0),
            "v9.0.0",
            "RBXDowngraderUpdate-win-x64.zip",
            new Uri("https://example.com/update.zip"),
            package.Length,
            new string('0', 64));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => service.PrepareAsync(release));

        Assert.Contains("hash", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsPayloadsThatAttemptToReplaceApplicationData()
    {
        using var temporary = new TemporaryDirectory();
        File.WriteAllBytes(Path.Combine(temporary.Path, "RBXDowngrader.exe"), [0]);
        File.WriteAllBytes(Path.Combine(temporary.Path, "uninstall.exe"), [0]);
        Directory.CreateDirectory(Path.Combine(temporary.Path, "robloxversions"));
        File.WriteAllBytes(Path.Combine(temporary.Path, "robloxversions", "data.bin"), [0]);
        var files = new[]
        {
            "RBXDowngrader.exe",
            "uninstall.exe",
            UpdateWorker.PayloadManifestName,
            Path.Combine("robloxversions", "data.bin")
        };
        File.WriteAllText(
            Path.Combine(temporary.Path, UpdateWorker.PayloadManifestName),
            JsonSerializer.Serialize(files));

        Assert.Throws<InvalidDataException>(() => UpdateWorker.ValidatePayload(temporary.Path));
    }

    private static byte[] CreateUpdatePackage()
    {
        var files = new[]
        {
            "RBXDowngrader.exe",
            "uninstall.exe",
            UpdateWorker.PayloadManifestName
        };
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "RBXDowngrader.exe", [1]);
            WriteEntry(archive, "uninstall.exe", [2]);
            WriteEntry(archive, UpdateWorker.PayloadManifestName, JsonSerializer.SerializeToUtf8Bytes(files));
        }
        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name);
        using var output = entry.Open();
        output.Write(content);
    }

    private static string RuntimeIdentifier() => RuntimeInformation.ProcessArchitecture == Architecture.Arm64
        ? "win-arm64"
        : "win-x64";
}
