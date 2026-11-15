using System.Text.Json;
using Microsoft.Extensions.Options;
using OtLab.Shared;

namespace OtLab.Gateway;

/// <summary>
/// Dziennik poleceń wydanych z HMI. Każde polecenie to jedna linia JSON dopisana
/// do pliku (format JSON Lines), więc plik da się czytać Notatnikiem i narzędziami.
/// W pamięci trzymamy tylko ostatnie N wpisów – tyle, ile pokazuje ekran.
/// </summary>
public sealed class CommandLog
{
    private readonly object _gate = new();
    private readonly Queue<CommandLogEntry> _recent = new();
    private readonly int _capacity;
    private readonly ILogger<CommandLog> _logger;

    public string FilePath { get; }

    public CommandLog(IOptions<GatewayOptions> options, IHostEnvironment env, ILogger<CommandLog> logger)
        : this(Path.GetFullPath(options.Value.CommandLogPath, env.ContentRootPath),
               options.Value.CommandLogOnScreen, logger)
    {
    }

    public CommandLog(string filePath, int capacity, ILogger<CommandLog> logger)
    {
        FilePath = filePath;
        _capacity = Math.Max(1, capacity);
        _logger = logger;

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        LoadTail();
        _logger.LogInformation("Dziennik poleceń: {Path} (wczytano {Count} ostatnich wpisów)",
            FilePath, _recent.Count);
    }

    /// <summary>Dopisuje wpis do pliku i do listy ostatnich wpisów.</summary>
    public void Append(CommandLogEntry entry)
    {
        string line = JsonSerializer.Serialize(entry, JsonContract.Options);
        lock (_gate)
        {
            try
            {
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch (IOException ex)
            {
                // Awaria dysku nie może zatrzymać sterowania; wpis zostaje przynajmniej na ekranie.
                _logger.LogError("Nie udało się zapisać dziennika poleceń: {Message}", ex.Message);
            }
            Remember(entry);
        }
    }

    /// <summary>Ostatnie wpisy, najstarszy pierwszy.</summary>
    public IReadOnlyList<CommandLogEntry> Recent()
    {
        lock (_gate) return _recent.ToArray();
    }

    // Po restarcie bramy ekran ma dalej pokazywać historię, więc czytamy koniec pliku.
    private void LoadTail()
    {
        if (!File.Exists(FilePath)) return;
        foreach (string line in File.ReadLines(FilePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                if (JsonSerializer.Deserialize<CommandLogEntry>(line, JsonContract.Options) is { } entry)
                    Remember(entry);
            }
            catch (JsonException)
            {
                // uszkodzona linia (np. po nagłym wyłączeniu) – pomijamy
            }
        }
    }

    private void Remember(CommandLogEntry entry)
    {
        _recent.Enqueue(entry);
        while (_recent.Count > _capacity) _recent.Dequeue();
    }
}
