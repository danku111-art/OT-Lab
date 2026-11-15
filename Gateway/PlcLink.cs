using System.Net.Sockets;
using Microsoft.Extensions.Options;
using NModbus;
using OtLab.Shared;

namespace OtLab.Gateway;

/// <summary>Stan PLC odczytany w jednym odpytaniu (mapa A).</summary>
public sealed record PlcSnapshot(
    PlcState State, int Rpm, int TempTenths, AlarmCode Alarm,
    int SpeedSetpoint, bool Running, bool EstopLatched, bool Fault);

/// <summary>
/// Połączenie brama -> PLC (brama jest masterem, mapa A).
/// Jedno połączenie TCP dzielą trzy źródła: odpytywanie stanu, heartbeat i polecenia
/// z HMI, dlatego każda operacja jest pod lockiem. Metody nie rzucają wyjątków;
/// po błędzie następne wywołanie łączy się od nowa.
/// </summary>
public sealed class PlcLink(IOptions<GatewayOptions> options, ILogger<PlcLink> logger) : IDisposable
{
    private readonly GatewayOptions _opt = options.Value;
    private readonly ModbusFactory _factory = new();
    private readonly object _gate = new();
    private TcpClient? _client;
    private IModbusMaster? _master;
    private bool _wasConnected;

    /// <summary>Zapis polecenia (FC05): ustawia coil na 1; skasuje go PLC.</summary>
    public bool TryWriteCommand(ushort coil, out string? error) =>
        Try(m => m.WriteSingleCoil(_opt.UnitId, coil, true), out error);

    /// <summary>Zapis nastawy prędkości (FC06).</summary>
    public bool TryWriteSpeedSetpoint(ushort rpm, out string? error) =>
        Try(m => m.WriteSingleRegister(_opt.UnitId, ModbusMapA.SpeedSetpoint, rpm), out error);

    /// <summary>Zapis licznika heartbeat (FC06).</summary>
    public bool TryWriteHeartbeat(ushort counter) =>
        Try(m => m.WriteSingleRegister(_opt.UnitId, ModbusMapA.Heartbeat, counter), out _);

    /// <summary>Odczyt stanów (FC02), rejestrów stanu (FC04) i nastawy (FC03). Null = brak łączności.</summary>
    public PlcSnapshot? TryReadStatus()
    {
        PlcSnapshot? snapshot = null;
        Try(m =>
        {
            bool[] bits = m.ReadInputs(_opt.UnitId, 0, ModbusMapA.DiscreteInputCount);
            ushort[] regs = m.ReadInputRegisters(_opt.UnitId, 0, ModbusMapA.InputRegisterCount);
            ushort setpoint = m.ReadHoldingRegisters(_opt.UnitId, ModbusMapA.SpeedSetpoint, 1)[0];
            snapshot = new PlcSnapshot(
                (PlcState)regs[ModbusMapA.State],
                regs[ModbusMapA.Rpm],
                regs[ModbusMapA.Temp],
                (AlarmCode)regs[ModbusMapA.AlarmCode],
                setpoint,
                bits[ModbusMapA.StRunning],
                bits[ModbusMapA.StEstop],
                bits[ModbusMapA.StFault]);
        }, out _);
        return snapshot;
    }

    private bool Try(Action<IModbusMaster> operation, out string? error)
    {
        lock (_gate)
        {
            try
            {
                operation(Master());
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = (ex as AggregateException)?.InnerException?.Message ?? ex.Message;
                if (_wasConnected)
                {
                    _wasConnected = false;
                    logger.LogWarning("Utracono połączenie z PLC: {Message}", error);
                }
                Close();
                return false;
            }
        }
    }

    private IModbusMaster Master()
    {
        if (_master is not null) return _master;

        var client = new TcpClient { NoDelay = true };
        try
        {
            if (!client.ConnectAsync(_opt.PlcHost, _opt.PlcPort).Wait(_opt.PlcTimeoutMs))
                throw new TimeoutException("Przekroczony czas łączenia z PLC.");
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _client = client;
        _master = _factory.CreateMaster(client);
        _master.Transport.ReadTimeout = _opt.PlcTimeoutMs;
        _master.Transport.WriteTimeout = _opt.PlcTimeoutMs;
        _master.Transport.Retries = 0;

        _wasConnected = true;
        logger.LogInformation("Połączono z PLC {Host}:{Port}", _opt.PlcHost, _opt.PlcPort);
        return _master;
    }

    private void Close()
    {
        _master?.Dispose();
        _client?.Dispose();
        _master = null;
        _client = null;
    }

    public void Dispose()
    {
        lock (_gate) Close();
    }
}
