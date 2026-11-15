using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using OtLab.Shared;

namespace OtLab.Gateway;

/// <summary>
/// Obsługa przeglądarek podłączonych przez WebSocket: przyjmuje polecenia,
/// przekłada je na zapis Modbus, zapisuje w dzienniku i rozsyła wiadomości.
/// Brama niczego nie ocenia – czy silnik ruszy, decyduje PLC.
/// </summary>
public sealed class HmiHub(PlcLink plc, CommandLog commandLog, ILogger<HmiHub> logger)
{
    private const int MaxMessageBytes = 1024;

    private readonly ConcurrentDictionary<Guid, Channel<string>> _clients = new();
    private volatile PlcSnapshot? _lastSnapshot;

    /// <summary>Ostatni stan PLC znany bramie (null = brak łączności). Ustawia go pętla odpytująca.</summary>
    public PlcSnapshot? LastSnapshot
    {
        get => _lastSnapshot;
        set => _lastSnapshot = value;
    }

    /// <summary>Wysyła wiadomość do wszystkich podłączonych HMI.</summary>
    public void Broadcast<T>(T message)
    {
        if (_clients.IsEmpty) return;
        string json = JsonSerializer.Serialize(message, JsonContract.Options);
        foreach (Channel<string> outbox in _clients.Values)
            outbox.Writer.TryWrite(json);
    }

    /// <summary>Obsługuje jedno połączenie HMI aż do jego zamknięcia.</summary>
    public async Task RunClientAsync(WebSocket socket, string source, CancellationToken ct)
    {
        // Każdy klient ma własną kolejkę wyjściową: przez WebSocket może pisać
        // tylko jeden nadawca naraz, a wiadomości powstają w kilku wątkach.
        var outbox = Channel.CreateBounded<string>(
            new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
        var id = Guid.NewGuid();
        _clients[id] = outbox;
        logger.LogInformation("HMI podłączone: {Source}", source);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task sender = SendLoopAsync(socket, outbox.Reader, cts.Token);
        try
        {
            // Na powitanie: historia poleceń.
            outbox.Writer.TryWrite(JsonSerializer.Serialize(
                new CommandLogMessage("commandLog", commandLog.Recent()), JsonContract.Options));

            await ReceiveLoopAsync(socket, source, cts.Token);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            // przeglądarka zamknięta albo brama się wyłącza
        }
        finally
        {
            _clients.TryRemove(id, out _);
            cts.Cancel();
            try { await sender; } catch (Exception) { /* nadawca kończy się razem z połączeniem */ }
            logger.LogInformation("HMI odłączone: {Source}", source);
        }
    }

    private static async Task SendLoopAsync(WebSocket socket, ChannelReader<string> outbox, CancellationToken ct)
    {
        await foreach (string json in outbox.ReadAllAsync(ct))
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, ct);
        }
    }

    private async Task ReceiveLoopAsync(WebSocket socket, string source, CancellationToken ct)
    {
        var buffer = new byte[MaxMessageBytes];
        while (socket.State == WebSocketState.Open)
        {
            int length = 0;
            WebSocketReceiveResult result;
            do
            {
                if (length == buffer.Length)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Wiadomość za długa.", ct);
                    return;
                }
                result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer, length, buffer.Length - length), ct);
                length += result.Count;
            }
            while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, ct);
                return;
            }
            if (result.MessageType != WebSocketMessageType.Text) continue;

            CommandLogEntry entry = Execute(Encoding.UTF8.GetString(buffer, 0, length), source);
            commandLog.Append(entry);
            Broadcast(new CommandEventMessage("command", entry));
        }
    }

    /// <summary>Przekłada jedną wiadomość JSON z HMI na zapis Modbus i opisuje wynik.</summary>
    private CommandLogEntry Execute(string json, string source)
    {
        CommandMessage? message = null;
        try
        {
            message = JsonSerializer.Deserialize<CommandMessage>(json, JsonContract.Options);
        }
        catch (JsonException)
        {
            // obsłużone niżej jako polecenie odrzucone
        }

        string cmd = message?.Cmd ?? "";
        int? value = null;
        CommandResult result;
        string? detail;

        switch (cmd)
        {
            case "start": (result, detail) = WriteCommand(ModbusMapA.CmdStart); break;
            case "stop": (result, detail) = WriteCommand(ModbusMapA.CmdStop); break;
            case "estop": (result, detail) = WriteCommand(ModbusMapA.CmdEstop); break;
            case "reset": (result, detail) = WriteCommand(ModbusMapA.CmdReset); break;

            case "setSpeed" when message!.Value is >= 0 and <= ushort.MaxValue:
                // Brama sprawdza tylko, czy liczba mieści się w rejestrze.
                // Zakres 0–3000 egzekwuje PLC.
                value = message.Value;
                (result, detail) = plc.TryWriteSpeedSetpoint((ushort)value.Value, out string? error)
                    ? (CommandResult.Sent, null)
                    : (CommandResult.Error, error);
                break;

            case "setSpeed":
                value = message!.Value;
                (result, detail) = (CommandResult.Rejected, "brak albo błędna wartość");
                break;

            default:
                (cmd, detail) = message is null
                    ? ("(nieczytelna wiadomość)", "to nie jest poprawny JSON")
                    : (Shorten(cmd), "nieznane polecenie");
                result = CommandResult.Rejected;
                break;
        }

        var entry = new CommandLogEntry(
            DateTimeOffset.Now, cmd, value, source, result, LastSnapshot?.State, detail);

        logger.LogInformation("Polecenie HMI {Cmd}{Value} od {Source}: {Result}{Detail}",
            cmd, value is null ? "" : $"={value}", source, result, detail is null ? "" : $" ({detail})");
        return entry;
    }

    private (CommandResult, string?) WriteCommand(ushort coil) =>
        plc.TryWriteCommand(coil, out string? error)
            ? (CommandResult.Sent, null)
            : (CommandResult.Error, error);

    private static string Shorten(string text) => text.Length <= 32 ? text : text[..32] + "…";
}
