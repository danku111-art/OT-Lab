namespace OtLab.Shared;

// Kształt wiadomości WebSocket HMI <-> brama (same dane, bez logiki).
// W JSON nazwy pól i wartości enumów są pisane camelCase; ustawia to brama.
// Przykłady wiadomości: docs/websocket.md.

/// <summary>
/// HMI -> brama. Cmd: "start", "stop", "estop", "reset" albo "setSpeed"
/// (wtedy Value = zadane obr/min).
/// </summary>
public record CommandMessage(string? Cmd, int? Value = null);

/// <summary>Brama -> HMI, co ok. 200 ms, gdy brama ma łączność z PLC.</summary>
public record StatusMessage(
    string Type, // zawsze "status"
    PlcState State,
    int Rpm,
    double TemperatureCelsius,
    AlarmCode Alarm,
    int SpeedSetpoint,
    bool Running,      // bit ST_RUNNING
    bool EstopLatched, // bit ST_ESTOP
    bool Fault);       // bit ST_FAULT

/// <summary>Brama -> HMI zamiast statusu, gdy brama nie ma łączności z PLC.</summary>
public record LinkMessage(
    string Type, // zawsze "link"
    bool PlcConnected);

/// <summary>Co brama zrobiła z poleceniem. Uwaga: Sent nie znaczy „wykonane” – o tym decyduje PLC.</summary>
public enum CommandResult
{
    Sent,     // zapisane do PLC
    Rejected, // nieznane albo błędne polecenie, nic nie wysłano
    Error     // brak łączności z PLC
}

/// <summary>Jeden wpis dziennika poleceń wydanych z HMI.</summary>
public record CommandLogEntry(
    DateTimeOffset Time,
    string Cmd,
    int? Value,            // tylko dla setSpeed
    string Source,         // adres przeglądarki, z której przyszło polecenie
    CommandResult Result,
    PlcState? StateBefore, // ostatni stan PLC znany bramie w chwili polecenia
    string? Detail);       // opis błędu, jeśli był

/// <summary>Brama -> HMI zaraz po połączeniu: ostatnie wpisy dziennika (najstarszy pierwszy).</summary>
public record CommandLogMessage(
    string Type, // zawsze "commandLog"
    IReadOnlyList<CommandLogEntry> Entries);

/// <summary>Brama -> wszystkie HMI: nowy wpis w dzienniku.</summary>
public record CommandEventMessage(
    string Type, // zawsze "command"
    CommandLogEntry Entry);
