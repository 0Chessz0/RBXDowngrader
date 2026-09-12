using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using DiscordRPC;
using RBXDowngrader.Core;

namespace RBXDowngrader;

public sealed class DiscordPresenceService : IDisposable
{
    // The Discord developer application's public Application ID. This is not a secret.
    public const string DiscordApplicationClientId = "1548394384410673303";
    private const string DownloadUrl =
        "https://github.com/0Chessz0/RBXDowngrader/releases/latest/download/RBXDowngraderSetup-win-x64.exe";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PresenceRefreshInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DiscordRetryInterval = TimeSpan.FromSeconds(10);
    private readonly object _sync = new();
    private readonly HttpClient _httpClient;
    private readonly RobloxGameResolver _gameResolver;
    private readonly string _logsDirectory;
    private CancellationTokenSource? _cancellation;
    private Task? _worker;

    public DiscordPresenceService(HttpClient? httpClient = null, string? logsDirectory = null)
    {
        _httpClient = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(AppIdentity.UserAgent);
        _gameResolver = new RobloxGameResolver(_httpClient);
        _logsDirectory = logsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Roblox",
            "logs");
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_worker is { IsCompleted: false })
                return;

            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            _worker = Task.Run(() => RunAsync(_cancellation.Token));
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        Task? worker;
        lock (_sync)
        {
            cancellation = _cancellation;
            worker = _worker;
            _cancellation = null;
            _worker = null;
            cancellation?.Cancel();
        }

        if (worker is not null)
        {
            try { await worker.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        cancellation?.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured())
        {
            WriteLog("Discord Rich Presence is enabled but no Discord Application ID is configured.");
            return;
        }

        DiscordRpcClient? client = null;
        string? activeLogPath = null;
        long activeLogPosition = 0;
        long? activePlaceId = null;
        string? currentPresenceDetails = null;
        var nextPresenceRefresh = DateTimeOffset.MinValue;
        var presenceIsSet = false;
        var discordUnavailableLogged = false;
        var missingLogLogged = false;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (client is null)
                {
                    client = TryConnect();
                    if (client is null)
                    {
                        if (!discordUnavailableLogged)
                        {
                            WriteLog("Discord Rich Presence could not connect. Discord may not be running.");
                            discordUnavailableLogged = true;
                        }
                        await Task.Delay(DiscordRetryInterval, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    discordUnavailableLogged = false;
                }

                if (!IsRobloxRunning())
                {
                    if (presenceIsSet)
                        TryClearPresence(client);
                    presenceIsSet = false;
                    activePlaceId = null;
                    currentPresenceDetails = null;
                    nextPresenceRefresh = DateTimeOffset.MinValue;
                    activeLogPath = null;
                    activeLogPosition = 0;
                    missingLogLogged = false;
                    await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!presenceIsSet)
                {
                    SetBrowsingPresence(client);
                    presenceIsSet = true;
                    currentPresenceDetails = "Browsing Roblox";
                    nextPresenceRefresh = DateTimeOffset.UtcNow + PresenceRefreshInterval;
                }

                if (presenceIsSet
                    && currentPresenceDetails is not null
                    && DateTimeOffset.UtcNow >= nextPresenceRefresh)
                {
                    SetPresence(client, currentPresenceDetails);
                    nextPresenceRefresh = DateTimeOffset.UtcNow + PresenceRefreshInterval;
                }

                var newestLog = FindNewestLog();
                if (newestLog is null)
                {
                    if (!missingLogLogged)
                    {
                        WriteLog($"Roblox is running, but no log file was found in '{_logsDirectory}'.");
                        missingLogLogged = true;
                    }
                    await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                missingLogLogged = false;

                if (!newestLog.Equals(activeLogPath, StringComparison.OrdinalIgnoreCase))
                {
                    activeLogPath = newestLog;
                    activeLogPosition = 0;
                }

                var readResult = await ReadActivityAsync(
                    activeLogPath,
                    activeLogPosition,
                    cancellationToken).ConfigureAwait(false);
                activeLogPosition = readResult.Position;

                if (readResult.Activity?.Kind == RobloxActivityKind.Left)
                {
                    SetBrowsingPresence(client);
                    presenceIsSet = true;
                    currentPresenceDetails = "Browsing Roblox";
                    nextPresenceRefresh = DateTimeOffset.UtcNow + PresenceRefreshInterval;
                    activePlaceId = null;
                }
                else if (readResult.Activity is { Kind: RobloxActivityKind.Joined, PlaceId: long placeId }
                         && placeId != activePlaceId)
                {
                    var gameName = "Roblox";
                    try
                    {
                        gameName = await _gameResolver.ResolveNameAsync(placeId, cancellationToken)
                            .ConfigureAwait(false) ?? gameName;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Could not resolve Roblox place {placeId}.", ex);
                    }

                    SetPlayingPresence(client, gameName);
                    activePlaceId = placeId;
                    presenceIsSet = true;
                    currentPresenceDetails = $"Playing {gameName}";
                    nextPresenceRefresh = DateTimeOffset.UtcNow + PresenceRefreshInterval;
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            WriteLog("Discord Rich Presence stopped unexpectedly.", ex);
        }
        finally
        {
            if (client is not null)
            {
                if (presenceIsSet)
                    TryClearPresence(client);
                try { client.Dispose(); }
                catch { }
            }
        }
    }

    private DiscordRpcClient? TryConnect()
    {
        DiscordRpcClient? client = null;
        try
        {
            client = new DiscordRpcClient(DiscordApplicationClientId);
            if (client.Initialize())
                return client;
        }
        catch (Exception ex)
        {
            WriteLog("Discord Rich Presence connection failed.", ex);
        }

        try { client?.Dispose(); }
        catch { }
        return null;
    }

    private string? FindNewestLog()
    {
        try
        {
            if (!Directory.Exists(_logsDirectory))
                return null;

            return Directory.EnumerateFiles(_logsDirectory, "*.log")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<LogReadResult> ReadActivityAsync(
        string path,
        long position,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length < position)
                position = 0;
            stream.Position = position;
            using var reader = new StreamReader(stream);
            RobloxActivity? latestActivity = null;
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
                latestActivity = RobloxLogActivity.Parse(line) ?? latestActivity;
            return new LogReadResult(stream.Position, latestActivity);
        }
        catch (FileNotFoundException)
        {
            return new LogReadResult(0, null);
        }
        catch (IOException)
        {
            return new LogReadResult(position, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new LogReadResult(position, null);
        }
    }

    private static bool IsRobloxRunning()
    {
        Process[] processes;
        try { processes = Process.GetProcessesByName("RobloxPlayerBeta"); }
        catch { return false; }

        try { return processes.Any(process => !process.HasExited); }
        catch { return false; }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    private static void SetBrowsingPresence(DiscordRpcClient client) =>
        SetPresence(client, "Browsing Roblox");

    private static void SetPlayingPresence(DiscordRpcClient client, string gameName) =>
        SetPresence(client, $"Playing {gameName}");

    private static void SetPresence(DiscordRpcClient client, string details) =>
        client.SetPresence(new RichPresence
        {
            Details = details,
            Buttons =
            [
                new Button
                {
                    Label = "Download",
                    Url = DownloadUrl
                }
            ]
        });

    private static void TryClearPresence(DiscordRpcClient client)
    {
        try { client.ClearPresence(); }
        catch { }
    }

    private static bool IsConfigured() =>
        ulong.TryParse(DiscordApplicationClientId, out var clientId) && clientId > 0;

    private static void WriteLog(string message, Exception? exception = null)
    {
        try
        {
            AppPaths.EnsureCreated();
            var detail = exception is null ? string.Empty : $" {exception.GetType().Name}: {exception.Message}";
            File.AppendAllText(
                AppPaths.LogFile,
                $"{DateTimeOffset.Now:O} [DiscordPresence] {message}{detail}{Environment.NewLine}");
        }
        catch { }
    }

    public void Dispose()
    {
        try { StopAsync().GetAwaiter().GetResult(); }
        catch { }
        _httpClient.Dispose();
    }

    private sealed record LogReadResult(long Position, RobloxActivity? Activity);
}
