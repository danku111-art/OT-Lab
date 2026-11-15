using System.Net.Sockets;
using NModbus;
using OtLab.Shared;

namespace OtLab.Plc;

/// <summary>
/// Połączenie PLC -> silnik (PLC jest tu masterem, mapa B).
/// Metody nie rzucają wyjątków: zwracają false, gdy silnik nie odpowiedział,
/// a przy następnym wywołaniu próbują połączyć się od nowa.
/// </summary>
public sealed class DriveLink(PlcOptions options, ILogger logger) : IDisposable
{
    private readonly ModbusFactory _factory = new();
    private TcpClient? _client;
    private IModbusMaster? _master;
    private bool _wasConnected;

    public bool TryReadMeasurements(out int rpm, out int tempTenths)
    {
        rpm = 0;
        tempTenths = 0;
        try
        {
            ushort[] r = Master().ReadInputRegisters(options.UnitId, ModbusMapB.Rpm, ModbusMapB.InputRegisterCount);
            rpm = r[ModbusMapB.Rpm];
            tempTenths = r[ModbusMapB.Temp];
            return true;
        }
        catch (Exception ex)
        {
            Drop(ex);
            return false;
        }
    }

    public bool TryWriteOutputs(bool run, bool brake, ushort speedRef)
    {
        try
        {
            IModbusMaster master = Master();
            master.WriteMultipleCoils(options.UnitId, ModbusMapB.Run, [run, brake]);
            master.WriteSingleRegister(options.UnitId, ModbusMapB.SpeedRef, speedRef);
            return true;
        }
        catch (Exception ex)
        {
            Drop(ex);
            return false;
        }
    }

    private IModbusMaster Master()
    {
        if (_master is not null) return _master;

        var client = new TcpClient { NoDelay = true };
        try
        {
            // Limit czasu jest potrzebny, bo na Windows odmowa połączenia z zamkniętym
            // portem localhost potrafi trwać ok. 2 s, a cykl PLC ma 100 ms.
            if (!client.ConnectAsync(options.MotorHost, options.MotorPort).Wait(options.DriveTimeoutMs))
                throw new TimeoutException("Przekroczony czas łączenia z silnikiem.");
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _client = client;
        _master = _factory.CreateMaster(client);
        _master.Transport.ReadTimeout = options.DriveTimeoutMs;
        _master.Transport.WriteTimeout = options.DriveTimeoutMs;
        _master.Transport.Retries = 0;

        _wasConnected = true;
        logger.LogInformation("Połączono z silnikiem {Host}:{Port}", options.MotorHost, options.MotorPort);
        return _master;
    }

    private void Drop(Exception ex)
    {
        if (_wasConnected)
        {
            _wasConnected = false;
            logger.LogWarning("Utracono połączenie z silnikiem: {Message}",
                (ex as AggregateException)?.InnerException?.Message ?? ex.Message);
        }
        Dispose();
    }

    public void Dispose()
    {
        _master?.Dispose();
        _client?.Dispose();
        _master = null;
        _client = null;
    }
}
