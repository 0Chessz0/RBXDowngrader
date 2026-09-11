using System.IO.Compression;
using RBXDowngrader.Core;

namespace RBXDowngrader.Core.Tests;

public sealed class VersionHashTests
{
    [Fact]
    public void NormalizesBareHashes()
    {
        Assert.True(VersionHash.TryNormalize("ABCDEF0123456789", out var result));
        Assert.Equal("version-abcdef0123456789", result);
    }

    [Fact]
    public void NormalizesPrefixedHashes()
    {
        Assert.True(VersionHash.TryNormalize(" version-0123456789abcdef ", out var result));
        Assert.Equal("version-0123456789abcdef", result);
    }

    [Fact]
    public void RejectsUnsafeVersionInput()
    {
        Assert.False(VersionHash.TryNormalize("../../Windows", out _));
        Assert.False(VersionHash.TryNormalize("version-short", out _));
    }

    [Fact]
    public void ExtractsVersionHashFromPastedText()
    {
        Assert.True(VersionHash.TryExtract(
            "Build: https://setup.rbxcdn.com/version-E7D81637D42C4B23-rbxPkgManifest.txt",
            out var result));
        Assert.Equal("version-e7d81637d42c4b23", result);
        Assert.False(VersionHash.TryExtract("0123456789abcdef0123456789abcdef", out _));
    }
}

public sealed class ModelTests
{
    [Fact]
    public void FormatsAggregateDiskSizes()
    {
        Assert.Equal("3.2 GB", FileSizeFormatter.Format(3_435_973_837));
        Assert.Equal("512 MB", FileSizeFormatter.Format(536_870_912));
    }
}

public sealed class RecentBuildServiceTests
{
    private const string HistoryJson = """
        {"platform":"windows","versions":[
          {"version":"version-0000000000000001","displayVersion":"0.5","liveAt":5000,"installable":true,"recommended":true,"lifecycle":"live"},
          {"version":"version-0000000000000002","displayVersion":"0.4","liveAt":4000,"installable":true,"recommended":false,"lifecycle":"superseded"},
          {"version":"version-0000000000000003","displayVersion":"0.3","liveAt":3000,"installable":true,"recommended":false,"lifecycle":"superseded"},
          {"version":"version-0000000000000004","displayVersion":"0.2","liveAt":2000,"installable":true,"recommended":false,"lifecycle":"superseded"},
          {"version":"version-0000000000000005","displayVersion":"0.1","liveAt":1000,"installable":true,"recommended":false,"lifecycle":"superseded"},
          {"version":"version-0000000000000006","displayVersion":"0.0","liveAt":0,"installable":true,"recommended":false,"lifecycle":"superseded"}
        ]}
        """;

    [Fact]
    public async Task ReadsTheFiveNewestBuildFeedEntries()
    {
        using var temporary = new TemporaryDirectory();
        using var client = new HttpClient(new DelegateHttpHandler((_, _) =>
            Task.FromResult(HttpResponses.Json(HistoryJson))));
        using var service = new RecentBuildService(client, Path.Combine(temporary.Path, "cache.json"));

        var result = await service.GetLatestAsync();

        Assert.False(result.IsCached);
        Assert.Equal(5, result.Builds.Count);
        Assert.Equal("version-0000000000000001", result.Builds[0].Version);
        Assert.True(result.Builds[0].IsCurrent);
    }

    [Fact]
    public async Task FallsBackToTheLastSuccessfulResponse()
    {
        using var temporary = new TemporaryDirectory();
        var cachePath = Path.Combine(temporary.Path, "cache.json");
        using (var liveClient = new HttpClient(new DelegateHttpHandler((_, _) =>
                   Task.FromResult(HttpResponses.Json(HistoryJson)))))
        using (var liveService = new RecentBuildService(liveClient, cachePath))
            Assert.False((await liveService.GetLatestAsync()).IsCached);

        using var offlineClient = new HttpClient(new DelegateHttpHandler((_, _) =>
            throw new HttpRequestException("offline")));
        using var offlineService = new RecentBuildService(offlineClient, cachePath);

        var result = await offlineService.GetLatestAsync();

        Assert.True(result.IsCached);
        Assert.Equal(5, result.Builds.Count);
    }
}

public sealed class PrivateServerLinkTests
{
    [Fact]
    public void AcceptsRobloxPrivateServerLinks()
    {
        Assert.True(PrivateServerLink.TryNormalize(
            "https://www.roblox.com/share?code=abc123&type=Server",
            out var webLink));
        Assert.Equal("https://www.roblox.com/share?code=abc123&type=Server", webLink);
        Assert.True(PrivateServerLink.TryNormalize("roblox://placeID=123", out _));
    }

    [Fact]
    public void RejectsNonRobloxPrivateServerLinks()
    {
        Assert.False(PrivateServerLink.TryNormalize("https://example.com/share?code=abc", out _));
        Assert.False(PrivateServerLink.TryNormalize("not a link", out _));
    }
}

public sealed class PackageManifestTests
{
    [Fact]
    public void ParsesManifestsWithAHeader()
    {
        var result = PackageManifest.Parse("v0\nRobloxApp.zip\n10\n20\n0123456789abcdef0123456789abcdef\n");
        Assert.Single(result);
        Assert.Equal("RobloxApp.zip", result[0].Name);
        Assert.Equal(20L, result[0].UncompressedSize);
    }

    [Fact]
    public void ParsesManifestsWithoutAHeader()
    {
        var result = PackageManifest.Parse("RobloxApp.zip\n10\n20\n0123456789abcdef0123456789abcdef\n");
        Assert.Single(result);
    }

    [Fact]
    public void ParsesCurrentChecksumFirstManifests()
    {
        var result = PackageManifest.Parse("v0\nRobloxApp.zip\ned10fd7bd5cf3d860a507af1708fd701\n135189909\n173265963\n");
        Assert.Single(result);
        Assert.Equal("ed10fd7bd5cf3d860a507af1708fd701", result[0].Checksum);
        Assert.Equal(135189909L, result[0].CompressedSize);
        Assert.Equal(173265963L, result[0].UncompressedSize);
    }

    [Fact]
    public void AcceptsTheCurrentPlayerInstallerPackage()
    {
        var result = PackageManifest.Parse("v0\nRobloxPlayerInstaller.exe\n0123456789abcdef0123456789abcdef\n100\n200\n");
        Assert.Equal("RobloxPlayerInstaller.exe", result[0].Name);
    }

    [Fact]
    public void RejectsMalformedManifests() =>
        Assert.Throws<FormatException>(() => PackageManifest.Parse("v0\nRobloxApp.zip\nbad\n20\nhash\n"));

    [Fact]
    public void RejectsUnsafePackageNames() =>
        Assert.Throws<FormatException>(() => PackageManifest.Parse("v0\n../payload.zip\n10\n20\nhash\n"));
}

public sealed class PackageMapTests
{
    [Fact]
    public void MapsPlayerPackages()
    {
        Assert.Equal(Path.Combine("content", "avatar"), PackageMap.GetExtractionDirectory("content-avatar.zip"));
        Assert.Equal(string.Empty, PackageMap.GetExtractionDirectory("RobloxApp.zip"));
    }
}

public sealed class SafeZipExtractorTests
{
    [Fact]
    public void BlocksZipPathTraversal()
    {
        using var temporary = new TemporaryDirectory();
        var archivePath = Path.Combine(temporary.Path, "unsafe.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            archive.CreateEntry("../escaped.txt");

        Assert.Throws<InvalidDataException>(() =>
            SafeZipExtractor.Extract(archivePath, Path.Combine(temporary.Path, "output")));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "escaped.txt")));
    }

    [Fact]
    public void AllowsSeparatorOnlyRootMarkers()
    {
        using var temporary = new TemporaryDirectory();
        var archivePath = Path.Combine(temporary.Path, "root-marker.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("\\");
            var file = archive.CreateEntry("content/test.txt");
            using var writer = new StreamWriter(file.Open());
            writer.Write("ok");
        }

        var output = Path.Combine(temporary.Path, "output");
        SafeZipExtractor.Extract(archivePath, output);
        Assert.Equal("ok", File.ReadAllText(Path.Combine(output, "content", "test.txt")));
    }

    [Fact]
    public void NormalizesRootedRobloxPackageEntries()
    {
        using var temporary = new TemporaryDirectory();
        var archivePath = Path.Combine(temporary.Path, "rooted-entry.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("\\abilities\\");
            var file = archive.CreateEntry("\\abilities\\config.json");
            using var writer = new StreamWriter(file.Open());
            writer.Write("{}");
        }

        var output = Path.Combine(temporary.Path, "output");
        SafeZipExtractor.Extract(archivePath, output);
        Assert.Equal("{}", File.ReadAllText(Path.Combine(output, "abilities", "config.json")));
    }

    [Fact]
    public void BlocksTraversalAfterLeadingSeparators()
    {
        using var temporary = new TemporaryDirectory();
        var archivePath = Path.Combine(temporary.Path, "rooted-traversal.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            archive.CreateEntry("\\..\\escaped.txt");

        Assert.Throws<InvalidDataException>(() =>
            SafeZipExtractor.Extract(archivePath, Path.Combine(temporary.Path, "output")));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "escaped.txt")));
    }
}
