using System.Text.Json;

namespace EppConfig.Services;

public sealed class PersistentLogService
{
    private const int MaxEntries = 2000;
    private const int TrimThreshold = 2400;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object _sync = new();
    private readonly string _logFilePath;

    public event Action<PersistentLogEntry>? LogAdded;

    public PersistentLogService()
    {
        var basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EppConfig",
            "logs");

        Directory.CreateDirectory(basePath);
        _logFilePath = Path.Combine(basePath, "application-log.jsonl");

        if (!File.Exists(_logFilePath))
        {
            File.WriteAllText(_logFilePath, string.Empty);
        }
    }

    public void Log(
        string level,
        string source,
        string message,
        string? blockType = null,
        string? blockId = null,
        string? bmk = null)
    {
        var entry = new PersistentLogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Level = string.IsNullOrWhiteSpace(level) ? "Info" : level,
            Source = string.IsNullOrWhiteSpace(source) ? "App" : source,
            Message = message,
            BlockType = blockType,
            BlockId = blockId,
            Bmk = bmk
        };

        lock (_sync)
        {
            var line = JsonSerializer.Serialize(entry, JsonOptions);
            File.AppendAllLines(_logFilePath, [line]);
            TrimIfNeededUnsafe();
        }

        LogAdded?.Invoke(entry);
    }

    public IReadOnlyList<PersistentLogEntry> GetLatest(int maxEntries = 300)
    {
        var take = maxEntries <= 0 ? 1 : maxEntries;

        lock (_sync)
        {
            if (!File.Exists(_logFilePath))
            {
                return [];
            }

            var lines = File.ReadLines(_logFilePath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .TakeLast(take)
                .ToList();

            var entries = new List<PersistentLogEntry>(lines.Count);
            foreach (var line in lines)
            {
                try
                {
                    var entry = JsonSerializer.Deserialize<PersistentLogEntry>(line, JsonOptions);
                    if (entry is not null)
                    {
                        entries.Add(entry);
                    }
                }
                catch
                {
                    // Ignoriere einzelne defekte Zeilen.
                }
            }

            entries.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
            return entries;
        }
    }

    private void TrimIfNeededUnsafe()
    {
        var lines = File.ReadAllLines(_logFilePath);
        if (lines.Length <= TrimThreshold)
        {
            return;
        }

        var trimmed = lines.TakeLast(MaxEntries);
        File.WriteAllLines(_logFilePath, trimmed);
    }
}

public sealed class PersistentLogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public string Level { get; init; } = "Info";
    public string Source { get; init; } = "App";
    public string Message { get; init; } = string.Empty;
    public string? BlockType { get; init; }
    public string? BlockId { get; init; }
    public string? Bmk { get; init; }
}
