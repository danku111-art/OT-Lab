using OtLab.Shared;

namespace OtLab.Motor;

/// <summary>Ustawienia z sekcji "Motor" w appsettings.json.</summary>
public sealed class MotorOptions
{
    public string ListenAddress { get; set; } = NetworkDefaults.Host;
    public int ListenPort { get; set; } = NetworkDefaults.MotorPort;
    public byte UnitId { get; set; } = NetworkDefaults.UnitId;
    public int StepMs { get; set; } = 50;
}
