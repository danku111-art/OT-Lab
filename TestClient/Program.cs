using System.Net.Sockets;
using NModbus;
using OtLab.Shared;

// Konsola diagnostyczna: rozmawia z PLC bezpośrednio przez Modbus TCP (mapa A),
// z pominięciem HMI i bramy. Przydaje się do testowania PLC, zanim brama będzie gotowa.
//
// Uwaga: TestClient nie wysyła heartbeatu, więc bez działającej bramy PLC po kilku
// sekundach zgłosi alarm „brak komunikacji z HMI” – i dobrze, tak ma być.

string host = args.Length > 0 ? args[0] : NetworkDefaults.Host;
int port = args.Length > 1 ? int.Parse(args[1]) : NetworkDefaults.PlcPort;
const byte Unit = NetworkDefaults.UnitId;

using var client = new TcpClient();
try
{
    client.Connect(host, port);
}
catch (SocketException ex)
{
    Console.WriteLine($"Nie można połączyć się z PLC {host}:{port}: {ex.Message}");
    return 1;
}
using IModbusMaster master = new ModbusFactory().CreateMaster(client);
master.Transport.ReadTimeout = 1000;

Console.WriteLine($"Połączono z PLC {host}:{port}.");
Console.WriteLine("Polecenia: start | stop | estop | reset | speed <obr/min> | status | q");

while (true)
{
    Console.Write("> ");
    string? line = Console.ReadLine();
    if (line is null) break;
    string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0) continue;

    try
    {
        switch (parts[0].ToLowerInvariant())
        {
            case "start": master.WriteSingleCoil(Unit, ModbusMapA.CmdStart, true); break;
            case "stop": master.WriteSingleCoil(Unit, ModbusMapA.CmdStop, true); break;
            case "estop": master.WriteSingleCoil(Unit, ModbusMapA.CmdEstop, true); break;
            case "reset": master.WriteSingleCoil(Unit, ModbusMapA.CmdReset, true); break;

            case "speed" when parts.Length == 2 && ushort.TryParse(parts[1], out ushort rpm):
                master.WriteSingleRegister(Unit, ModbusMapA.SpeedSetpoint, rpm);
                break;

            case "status":
                bool[] bits = master.ReadInputs(Unit, 0, ModbusMapA.DiscreteInputCount);
                ushort[] regs = master.ReadInputRegisters(Unit, 0, ModbusMapA.InputRegisterCount);
                ushort[] hold = master.ReadHoldingRegisters(Unit, 0, ModbusMapA.HoldingRegisterCount);
                Console.WriteLine($"  STATE      = {(PlcState)regs[ModbusMapA.State]}");
                Console.WriteLine($"  RPM        = {regs[ModbusMapA.Rpm]} obr/min");
                Console.WriteLine($"  TEMP       = {regs[ModbusMapA.Temp] / (double)NetworkDefaults.TempScale:F1} °C");
                Console.WriteLine($"  ALARM_CODE = {(AlarmCode)regs[ModbusMapA.AlarmCode]}");
                Console.WriteLine($"  ST_RUNNING = {bits[ModbusMapA.StRunning]}, ST_ESTOP = {bits[ModbusMapA.StEstop]}, ST_FAULT = {bits[ModbusMapA.StFault]}");
                Console.WriteLine($"  SPEED_SETPOINT = {hold[ModbusMapA.SpeedSetpoint]}, HEARTBEAT = {hold[ModbusMapA.Heartbeat]}");
                break;

            case "q": return 0;

            default: Console.WriteLine("  Nieznane polecenie."); break;
        }
    }
    catch (SlaveException ex)
    {
        Console.WriteLine($"  PLC odpowiedział wyjątkiem Modbus: {ex.Message}");
    }
    catch (Exception ex) when (ex is IOException or SocketException or TimeoutException)
    {
        Console.WriteLine($"  Utracono połączenie z PLC: {ex.Message}");
        return 1;
    }
}
return 0;
