using OtLab.Shared;

namespace OtLab.Plc;

/// <summary>Ustawienia z sekcji "Plc" w appsettings.json.</summary>
public sealed class PlcOptions
{
    public string ListenAddress { get; set; } = NetworkDefaults.Host;
    public int ListenPort { get; set; } = NetworkDefaults.PlcPort;
    public string MotorHost { get; set; } = NetworkDefaults.Host;
    public int MotorPort { get; set; } = NetworkDefaults.MotorPort;
    public byte UnitId { get; set; } = NetworkDefaults.UnitId;
    public int CycleMs { get; set; } = 100;

    /// <summary>Limit czasu jednej operacji Modbus do silnika (połączenie, zapis, odczyt).</summary>
    public int DriveTimeoutMs { get; set; } = 80;
}
