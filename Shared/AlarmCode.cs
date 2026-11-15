namespace OtLab.Shared;

/// <summary>Kod alarmu publikowany przez PLC w rejestrze ALARM_CODE (mapa A).</summary>
public enum AlarmCode : ushort
{
    None = 0,
    Overheat = 1,      // przegrzanie silnika
    HmiCommLost = 2,   // brak komunikacji z HMI (bramą)
    DriveCommLost = 3  // brak komunikacji z napędem
}
