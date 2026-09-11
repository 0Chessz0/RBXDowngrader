using System.Text.Json;

namespace RBXDowngrader.Core;

public sealed class UpdateCheckThrottle
{
    public const int MaxChecksPerWindow = 20;
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private static readonly object Gate = new();
    private readonly string _logPath;
    private readonly TimeProvider _clock;

    public UpdateCheckThrottle(string? logPath = null, TimeProvider? clock = null)
    {
        _logPath = logPath ?? AppPaths.UpdateCheckLog;
        _clock = clock ?? TimeProvider.System;
    }

    public bool TryAcquire(out TimeSpan retryAfter)
    {
        lock (Gate)
        {
            var now = _clock.GetUtcNow();
            var timestamps = ReadTimestamps()
                .Where(timestamp => now - timestamp < Window)
                .OrderBy(timestamp => timestamp)
                .ToList();

            if (timestamps.Count >= MaxChecksPerWindow)
            {
                retryAfter = timestamps[0] + Window - now;
                if (retryAfter < TimeSpan.Zero)
                    retryAfter = TimeSpan.Zero;
                return false;
            }

            timestamps.Add(now);
            WriteTimestamps(timestamps);
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    private List<DateTimeOffset> ReadTimestamps()
    {
        try
        {
            if (!File.Exists(_logPath))
                return [];

            return JsonSerializer.Deserialize<List<DateTimeOffset>>(File.ReadAllText(_logPath)) ?? [];
        }
        catch (IOException) { return []; }
        catch (JsonException) { return []; }
    }

    private void WriteTimestamps(IReadOnlyList<DateTimeOffset> timestamps)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        var temporaryPath = $"{_logPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(timestamps));
            File.Move(temporaryPath, _logPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (IOException) { }
        }
    }
}
