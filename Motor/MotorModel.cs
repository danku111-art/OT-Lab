using OtLab.Shared;

namespace OtLab.Motor;

/// <summary>
/// Cyfrowy bliźniak silnika DC: sama fizyka, bez Modbusa i bez zegara.
/// Czas dostaje z zewnątrz (dt), dzięki czemu testy nie muszą czekać.
/// </summary>
public sealed class MotorModel : IMotor
{
    public const double AccelRpmPerSec = 1000; // rozpędzanie przy RUN = 1
    public const double CoastRpmPerSec = 500;  // wybieg przy RUN = 0
    public const double BrakeRpmPerSec = 2000; // hamowanie przy BRAKE = 1

    public const double AmbientCelsius = 22;
    public const double HeatAtMaxSpeedCelsius = 40;  // o tyle cieplej przy 3000 obr/min
    public const double OverheatTargetCelsius = 120; // dokąd dąży temperatura przy „przegrzej”
    public const double ThermalTimeConstantSec = 20;

    private double _rpm;
    private double _tempCelsius = AmbientCelsius;

    /// <summary>Przełącznik „przegrzej” z konsoli silnika (symulacja usterki chłodzenia).</summary>
    public bool Overheat { get; set; }

    public int Rpm => (int)Math.Round(_rpm);

    public int TempTenths => (int)Math.Round(_tempCelsius * NetworkDefaults.TempScale);

    public void Update(bool run, bool brake, int speedRef, TimeSpan dt)
    {
        double seconds = dt.TotalSeconds;

        if (brake)
        {
            // Hamulec jest ważniejszy niż RUN.
            _rpm = Math.Max(0, _rpm - BrakeRpmPerSec * seconds);
        }
        else if (run)
        {
            double target = Math.Clamp(speedRef, 0, NetworkDefaults.MaxSpeedRpm);
            _rpm = _rpm < target
                ? Math.Min(target, _rpm + AccelRpmPerSec * seconds)
                : Math.Max(target, _rpm - CoastRpmPerSec * seconds);
        }
        else
        {
            _rpm = Math.Max(0, _rpm - CoastRpmPerSec * seconds);
        }

        // Temperatura dąży do wartości zależnej od obrotów (inercja pierwszego rzędu).
        double targetTemp = Overheat
            ? OverheatTargetCelsius
            : AmbientCelsius + HeatAtMaxSpeedCelsius * _rpm / NetworkDefaults.MaxSpeedRpm;
        _tempCelsius += (targetTemp - _tempCelsius) * (1 - Math.Exp(-seconds / ThermalTimeConstantSec));
    }
}
