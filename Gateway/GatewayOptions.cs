using OtLab.Shared;

namespace OtLab.Gateway;

/// <summary>Ustawienia z sekcji "Gateway" w appsettings.json.</summary>
public sealed class GatewayOptions
{
    public string PlcHost { get; set; } = NetworkDefaults.Host;
    public int PlcPort { get; set; } = NetworkDefaults.PlcPort;
    public byte UnitId { get; set; } = NetworkDefaults.UnitId;

    public int PollMs { get; set; } = 200;
    public int HeartbeatMs { get; set; } = 500;
    public int PlcTimeoutMs { get; set; } = 300;

    /// <summary>Plik dziennika poleceń (JSON Lines), ścieżka względem katalogu projektu.</summary>
    public string CommandLogPath { get; set; } = "logs/commands.jsonl";

    /// <summary>Ile ostatnich poleceń brama trzyma w pamięci i wysyła na ekran HMI.</summary>
    public int CommandLogOnScreen { get; set; } = 100;
}
