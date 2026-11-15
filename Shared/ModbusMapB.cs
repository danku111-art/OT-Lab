namespace OtLab.Shared;

/// <summary>
/// Mapa B: PLC (master) -> silnik (slave), port 5021.
/// Te tabele leżą w pamięci procesu silnika. Adresy liczone od 0.
/// </summary>
public static class ModbusMapB
{
    // Coils: polecenia od PLC.
    public const ushort Run = 0;   // 1 = pracuj, 0 = wybieg do zatrzymania
    public const ushort Brake = 1; // 1 = szybkie hamowanie (e-stop)
    public const ushort CoilCount = 2;

    // Holding registers: nastawy od PLC.
    public const ushort SpeedRef = 0; // obr/min, 0–3000

    // Input registers: pomiary z silnika.
    public const ushort Rpm = 0;  // obr/min
    public const ushort Temp = 1; // 0,1 °C (np. 253 = 25,3 °C)
    public const ushort InputRegisterCount = 2;
}
