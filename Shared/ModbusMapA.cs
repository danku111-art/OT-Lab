namespace OtLab.Shared;

/// <summary>
/// Mapa A: brama (master) -> PLC (slave), port 5020.
/// Te tabele leżą w pamięci procesu PLC. Adresy liczone od 0.
/// </summary>
public static class ModbusMapA
{
    // Coils: polecenia od bramy. Działają jak dzwonek: brama ustawia 1,
    // PLC odczytuje i kasuje na 0 w swoim cyklu.
    public const ushort CmdStart = 0;
    public const ushort CmdStop = 1;
    public const ushort CmdEstop = 2;
    public const ushort CmdReset = 3;
    public const ushort CoilCount = 4;

    // Discrete inputs: stany ustawiane przez PLC (brama tylko czyta).
    public const ushort StRunning = 0; // 1 = PLC każe silnikowi pracować (RUN = 1)
    public const ushort StEstop = 1;   // 1 = e-stop zatrzaśnięty
    public const ushort StFault = 2;   // 1 = aktywna awaria (ALARM_CODE != 0)
    public const ushort DiscreteInputCount = 3;

    // Input registers: stan i pomiary (brama tylko czyta).
    public const ushort State = 0;     // wartości PlcState
    public const ushort Rpm = 1;       // obr/min (kopia z silnika)
    public const ushort Temp = 2;      // 0,1 °C (kopia z silnika)
    public const ushort AlarmCode = 3; // wartości AlarmCode
    public const ushort InputRegisterCount = 4;

    // Holding registers: ustawienia od bramy.
    public const ushort SpeedSetpoint = 0; // obr/min, 0–3000
    public const ushort Heartbeat = 1;     // licznik zwiększany przez bramę co 500 ms
    public const ushort HoldingRegisterCount = 2;
}
