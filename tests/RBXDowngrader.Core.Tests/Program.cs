using System.IO.Compression;
using RBXDowngrader.Core;

var tests = new (string Name, Action Run)[]
{
    ("normalizes bare hashes", () =>
    {
        Expect(VersionHash.TryNormalize("ABCDEF0123456789", out var result));
        ExpectEqual("version-abcdef0123456789", result);
    }),
    ("normalizes prefixed hashes", () =>
    {
        Expect(VersionHash.TryNormalize(" version-0123456789abcdef ", out var result));
        ExpectEqual("version-0123456789abcdef", result);
    }),
    ("rejects unsafe version input", () =>
    {
        Expect(!VersionHash.TryNormalize("../../Windows", out _));
        Expect(!VersionHash.TryNormalize("version-short", out _));
    }),
    ("extracts a version hash from pasted text", () =>
    {
        Expect(VersionHash.TryExtract(
            "Build: https://setup.rbxcdn.com/version-E7D81637D42C4B23-rbxPkgManifest.txt",
            out var result));
        ExpectEqual("version-e7d81637d42c4b23", result);
        Expect(!VersionHash.TryExtract("0123456789abcdef0123456789abcdef", out _));
    }),
    ("formats aggregate disk sizes", () =>
    {
        ExpectEqual("3.2 GB", FileSizeFormatter.Format(3_435_973_837));
        ExpectEqual("512 MB", FileSizeFormatter.Format(536_870_912));
    }),
    ("reads the five newest build feed entries", () =>
    {
        const string json = """
            {"platform":"windows","versions":[
              {"version":"version-0000000000000001","displayVersion":"0.5","liveAt":5000,"installable":true,"recommended":true,"lifecycle":"live"},
              {"version":"version-0000000000000002","displayVersion":"0.4","liveAt":4000,"installable":true,"recommended":false,"lifecycle":"superseded"},
              {"version":"version-0000000000000003","displayVersion":"0.3","liveAt":3000,"installable":true,"recommended":false,"lifecycle":"superseded"},
              {"version":"version-0000000000000004","displayVersion":"0.2","liveAt":2000,"installable":true,"recommended":false,"lifecycle":"superseded"},
              {"version":"version-0000000000000005","displayVersion":"0.1","liveAt":1000,"installable":true,"recommended":false,"lifecycle":"superseded"},
              {"version":"version-0000000000000006","displayVersion":"0.0","liveAt":0,"installable":true,"recommended":false,"lifecycle":"superseded"}
            ]}
            """;
        using var client = new HttpClient(new StaticJsonHandler(json));
        using var service = new RecentBuildService(client);
        var result = service.GetLatestAsync().GetAwaiter().GetResult();
        ExpectEqual(5, result.Count);
        ExpectEqual("version-0000000000000001", result[0].Version);
        Expect(result[0].IsCurrent);
    }),
    ("accepts Roblox private server links", () =>
    {
        Expect(PrivateServerLink.TryNormalize(
            "https://www.roblox.com/share?code=abc123&type=Server",
            out var webLink));
        ExpectEqual("https://www.roblox.com/share?code=abc123&type=Server", webLink);
        Expect(PrivateServerLink.TryNormalize("roblox://placeID=123", out _));
    }),
    ("rejects non-Roblox private server links", () =>
    {
        Expect(!PrivateServerLink.TryNormalize("https://example.com/share?code=abc", out _));
        Expect(!PrivateServerLink.TryNormalize("not a link", out _));
    }),
    ("parses manifests with a header", () =>
    {
        var result = PackageManifest.Parse("v0\nRobloxApp.zip\n10\n20\n0123456789abcdef0123456789abcdef\n");
        ExpectEqual(1, result.Count);
        ExpectEqual("RobloxApp.zip", result[0].Name);
        ExpectEqual(20L, result[0].UncompressedSize);
    }),
    ("parses manifests without a header", () =>
    {
        var result = PackageManifest.Parse("RobloxApp.zip\n10\n20\n0123456789abcdef0123456789abcdef\n");
        ExpectEqual(1, result.Count);
    }),
    ("parses current checksum-first manifests", () =>
    {
        var result = PackageManifest.Parse("v0\nRobloxApp.zip\ned10fd7bd5cf3d860a507af1708fd701\n135189909\n173265963\n");
        ExpectEqual(1, result.Count);
        ExpectEqual("ed10fd7bd5cf3d860a507af1708fd701", result[0].Checksum);
        ExpectEqual(135189909L, result[0].CompressedSize);
        ExpectEqual(173265963L, result[0].UncompressedSize);
    }),
    ("accepts the current player installer package", () =>
    {
        var result = PackageManifest.Parse("v0\nRobloxPlayerInstaller.exe\n0123456789abcdef0123456789abcdef\n100\n200\n");
        ExpectEqual("RobloxPlayerInstaller.exe", result[0].Name);
    }),
    ("rejects malformed manifests", () =>
    {
        ExpectThrows<FormatException>(() => PackageManifest.Parse("v0\nRobloxApp.zip\nbad\n20\nhash\n"));
    }),
    ("rejects unsafe package names", () =>
    {
        ExpectThrows<FormatException>(() => PackageManifest.Parse("v0\n../payload.zip\n10\n20\nhash\n"));
    }),
    ("maps player packages", () =>
    {
        ExpectEqual(Path.Combine("content", "avatar"), PackageMap.GetExtractionDirectory("content-avatar.zip"));
        ExpectEqual(string.Empty, PackageMap.GetExtractionDirectory("RobloxApp.zip"));
    }),
    ("blocks zip path traversal", () =>
    {
        var root = Path.Combine(Path.GetTempPath(), $"rbxd-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var archivePath = Path.Combine(root, "unsafe.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
                archive.CreateEntry("../escaped.txt");
            ExpectThrows<InvalidDataException>(() => SafeZipExtractor.Extract(archivePath, Path.Combine(root, "output")));
            Expect(!File.Exists(Path.Combine(root, "escaped.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }),
    ("allows separator-only root markers", () =>
    {
        var root = Path.Combine(Path.GetTempPath(), $"rbxd-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var archivePath = Path.Combine(root, "root-marker.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                archive.CreateEntry("\\");
                var file = archive.CreateEntry("content/test.txt");
                using var writer = new StreamWriter(file.Open());
                writer.Write("ok");
            }

            var output = Path.Combine(root, "output");
            SafeZipExtractor.Extract(archivePath, output);
            ExpectEqual("ok", File.ReadAllText(Path.Combine(output, "content", "test.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }),
    ("normalizes rooted Roblox package entries", () =>
    {
        var root = Path.Combine(Path.GetTempPath(), $"rbxd-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var archivePath = Path.Combine(root, "rooted-entry.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                archive.CreateEntry("\\abilities\\");
                var file = archive.CreateEntry("\\abilities\\config.json");
                using var writer = new StreamWriter(file.Open());
                writer.Write("{}");
            }

            var output = Path.Combine(root, "output");
            SafeZipExtractor.Extract(archivePath, output);
            ExpectEqual("{}", File.ReadAllText(Path.Combine(output, "abilities", "config.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }),
    ("blocks traversal after leading separators", () =>
    {
        var root = Path.Combine(Path.GetTempPath(), $"rbxd-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var archivePath = Path.Combine(root, "rooted-traversal.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
                archive.CreateEntry("\\..\\escaped.txt");
            ExpectThrows<InvalidDataException>(() => SafeZipExtractor.Extract(archivePath, Path.Combine(root, "output")));
            Expect(!File.Exists(Path.Combine(root, "escaped.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    })
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL  {name}: {ex.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed");
return failures == 0 ? 0 : 1;

static void Expect(bool condition)
{
    if (!condition)
        throw new InvalidOperationException("Expectation was false.");
}

static void ExpectEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}

static void ExpectThrows<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

sealed class StaticJsonHandler(string json) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
}
