namespace OtLab.Shared;

/// <summary>
/// Wartości domyślne. Porty i adresy można nadpisać w appsettings.json,
/// ale adresy rejestrów zmienia się tylko w ModbusMapA / ModbusMapB.
/// </summary>
public static class NetworkDefaults
{
    public const string Host = "127.0.0.1";
    public const int PlcPort = 5020;
    public const int MotorPort = 5021;
    public const int GatewayPort = 8080;
    public const byte UnitId = 1;

    public const int TempScale = 10; // TEMP w rejestrze = °C * 10
    public const ushort DefaultSpeedRpm = 1500;
    public const ushort MaxSpeedRpm = 3000;
}
