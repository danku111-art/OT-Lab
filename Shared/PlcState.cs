namespace OtLab.Shared;

/// <summary>Stan publikowany przez PLC w rejestrze STATE (mapa A).</summary>
public enum PlcState : ushort
{
    Stopped = 0,  // stoi
    Starting = 1, // rozpędza się
    Running = 2,  // pracuje
    Braking = 3,  // hamuje
    Estop = 4,    // e-stop zatrzaśnięty
    Fault = 5     // awaria
}
