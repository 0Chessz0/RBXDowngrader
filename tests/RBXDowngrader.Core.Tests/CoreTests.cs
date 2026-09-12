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

public sealed class VersionShortcutServiceTests
{
    [Fact]
    public void ShortcutSurvivesRenameAndIsRemovedWithTheVersion()
    {
        using var temporary = new TemporaryDirectory();
        var desktop = Path.Combine(temporary.Path, "Desktop");
        var startMenu = Path.Combine(temporary.Path, "StartMenu");
        var versionDirectory = Path.Combine(temporary.Path, "version-0123456789abcdef");
        Directory.CreateDirectory(versionDirectory);
        var executable = Path.Combine(versionDirectory, "RobloxPlayerBeta.exe");
        File.WriteAllBytes(executable, [0]);
        var original = new InstalledVersion(
            "version-0123456789abcdef",
            versionDirectory,
            DateTimeOffset.UtcNow,
            1,
            executable);
        var renamed = original with { CustomName = "Classic Roblox" };
        var service = new VersionShortcutService(desktop, startMenu);

        service.SetState(original, desktop: true, startMenu: true);
        service.RenameExisting(original, renamed);

        Assert.False(File.Exists(Path.Combine(desktop, "0123456789abcdef.lnk")));
        Assert.False(File.Exists(Path.Combine(startMenu, "0123456789abcdef.lnk")));
        Assert.True(File.Exists(Path.Combine(desktop, "Classic Roblox.lnk")));
        Assert.True(File.Exists(Path.Combine(startMenu, "Classic Roblox.lnk")));
        Assert.Equal(new VersionShortcutState(true, true), service.GetState(renamed));

        service.Remove(renamed);

        Assert.Equal(new VersionShortcutState(false, false), service.GetState(renamed));
    }

    [Fact]
    public void RenameDoesNotCreateMissingShortcuts()
    {
        using var temporary = new TemporaryDirectory();
        var service = new VersionShortcutService(
            Path.Combine(temporary.Path, "Desktop"),
            Path.Combine(temporary.Path, "StartMenu"));
        var version = new InstalledVersion(
            "version-0123456789abcdef",
            temporary.Path,
            DateTimeOffset.UtcNow,
            0,
            Path.Combine(temporary.Path, "RobloxPlayerBeta.exe"));

        service.RenameExisting(version, version with { CustomName = "Renamed" });

        Assert.Equal(
            new VersionShortcutState(false, false),
            service.GetState(version with { CustomName = "Renamed" }));
    }
}

public sealed class UpdateCheckThrottleTests
{
    [Fact]
    public void AllowsTwentyChecksAndBlocksTheTwentyFirst()
    {
        using var temporary = new TemporaryDirectory();
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var throttle = new UpdateCheckThrottle(Path.Combine(temporary.Path, "checks.json"), clock);

        for (var i = 0; i < UpdateCheckThrottle.MaxChecksPerWindow; i++)
            Assert.True(throttle.TryAcquire(out _));

        Assert.False(throttle.TryAcquire(out var retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
    }

    [Fact]
    public void ExpiresChecksAfterTheRollingWindow()
    {
        using var temporary = new TemporaryDirectory();
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var throttle = new UpdateCheckThrottle(Path.Combine(temporary.Path, "checks.json"), clock);

        for (var i = 0; i < UpdateCheckThrottle.MaxChecksPerWindow; i++)
            Assert.True(throttle.TryAcquire(out _));

        clock.Advance(UpdateCheckThrottle.Window + TimeSpan.FromSeconds(1));

        Assert.True(throttle.TryAcquire(out _));
    }
}

public sealed class UpdateSkipStoreTests
{
    [Fact]
    public void SkipsTheSavedVersionAndOlderVersions()
    {
        using var temporary = new TemporaryDirectory();
        var store = new UpdateSkipStore(Path.Combine(temporary.Path, "skipped.txt"));

        store.Skip(new Version(2, 0, 0));

        Assert.True(store.IsSkipped(new Version(2, 0, 0)));
        Assert.True(store.IsSkipped(new Version(1, 9, 0)));
        Assert.False(store.IsSkipped(new Version(2, 1, 0)));
    }
}

public sealed class ApplicationDataPathRulesTests
{
    [Theory]
    [InlineData("settings.json")]
    [InlineData(@"robloxversions\version-0123456789abcdef\RobloxPlayerBeta.exe")]
    public void ProtectsSettingsAndDownloadedVersionsFromUpdatePayloads(string path) =>
        Assert.True(ApplicationDataPathRules.IsProtectedPath(path));

    [Theory]
    [InlineData("robloxversions", true)]
    [InlineData("temp", true)]
    [InlineData("RBXDowngrader.log", false)]
    [InlineData("recent-builds-cache.json", false)]
    [InlineData("update-checks.json", false)]
    [InlineData("settings.json", false)]
    public void AllowsKnownDataLeftovers(string path, bool isDirectory) =>
        Assert.True(ApplicationDataPathRules.IsAllowedExistingEntry(path, isDirectory));

    [Theory]
    [InlineData("other-folder", true)]
    [InlineData("unknown.txt", false)]
    public void BlocksUnknownInstallDirectoryEntries(string path, bool isDirectory) =>
        Assert.False(ApplicationDataPathRules.IsAllowedExistingEntry(path, isDirectory));
}

public sealed class SettingsStoreTests
{
    [Fact]
    public void UsesEnabledDefaultsWhenNoSettingsExist()
    {
        using var temporary = new TemporaryDirectory();
        var store = new SettingsStore(Path.Combine(temporary.Path, "settings.json"));

        var settings = store.Load();

        Assert.True(settings.DiscordRichPresenceEnabled);
        Assert.True(settings.MinimizeToTrayOnLaunch);
        Assert.True(settings.AutomaticUpdateChecksEnabled);
    }

    [Fact]
    public void PersistsPreferences()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "settings.json");
        var store = new SettingsStore(path);
        var expected = new AppSettings
        {
            DiscordRichPresenceEnabled = false,
            MinimizeToTrayOnLaunch = false,
            AutomaticUpdateChecksEnabled = false
        };

        store.Save(expected);

        Assert.Equal(expected, store.Load());
    }

    [Fact]
    public void FallsBackToDefaultsForInvalidJson()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "settings.json");
        File.WriteAllText(path, "not json");

        var settings = new SettingsStore(path).Load();

        Assert.True(settings.DiscordRichPresenceEnabled);
        Assert.True(settings.MinimizeToTrayOnLaunch);
        Assert.True(settings.AutomaticUpdateChecksEnabled);
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

public sealed class RobloxLogActivityTests
{
    [Theory]
    [InlineData("[FLog::Output] ! Joining game '695c6db6' place 9391468976 at 10.9.7.217", 9391468976)]
    [InlineData("[FLog::GameJoinLoadTime] Report game_join_loadtime: placeid:9391468976, universeid:3508322461", 9391468976)]
    public void ReadsPlaceIdsFromRobloxJoinLines(string line, long expectedPlaceId)
    {
        var activity = RobloxLogActivity.Parse(line);

        Assert.NotNull(activity);
        Assert.Equal(RobloxActivityKind.Joined, activity.Kind);
        Assert.Equal(expectedPlaceId, activity.PlaceId);
    }

    [Theory]
    [InlineData("[FLog::Output] leaveUGCGameInternal")]
    [InlineData("[FLog::Network] NetworkClient:Remove")]
    public void DetectsLeavingAGame(string line) =>
        Assert.Equal(RobloxActivityKind.Left, RobloxLogActivity.Parse(line)?.Kind);
}

public sealed class RobloxGameResolverTests
{
    [Fact]
    public async Task ResolvesPlaceToUniverseToGameName()
    {
        var requests = new List<Uri>();
        using var client = new HttpClient(new DelegateHttpHandler((request, _) =>
        {
            requests.Add(request.RequestUri!);
            return Task.FromResult(request.RequestUri!.Host == "apis.roblox.com"
                ? HttpResponses.Json("{\"universeId\":3508322461}")
                : HttpResponses.Json("{\"data\":[{\"name\":\"Example Experience\"}]}") );
        }));
        var resolver = new RobloxGameResolver(client);

        var name = await resolver.ResolveNameAsync(9391468976);

        Assert.Equal("Example Experience", name);
        Assert.Equal(2, requests.Count);
        Assert.Contains("/places/9391468976/universe", requests[0].AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("universeIds=3508322461", requests[1].Query, StringComparison.Ordinal);
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
