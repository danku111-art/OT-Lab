using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using NModbus;
using OtLab.Shared;

namespace OtLab.Plc;

/// <summary>
/// Cykl sterownika: co 100 ms odczyt wejść, logika, zapis wyjść.
/// Wobec bramy PLC jest slave'em (mapa A), wobec silnika masterem (mapa B).
/// </summary>
public sealed class PlcService(
    PlcMemory memory,
    PlcLogic logic,
    IOptions<PlcOptions> options,
    ILogger<PlcService> logger) : BackgroundService
{
    private readonly PlcOptions _opt = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Strona slave: NModbus sam obsługuje zapytania bramy na tabelach PlcMemory.
        var listener = new TcpListener(IPAddress.Parse(_opt.ListenAddress), _opt.ListenPort);
        listener.Start();
        var factory = new ModbusFactory();
        var network = factory.CreateSlaveNetwork(listener);
        network.AddSlave(factory.CreateSlave(_opt.UnitId, memory));
        _ = network.ListenAsync(stoppingToken);
        logger.LogInformation("PLC nasłuchuje Modbus TCP na {Address}:{Port} (mapa A), cykl {Cycle} ms",
            _opt.ListenAddress, _opt.ListenPort, _opt.CycleMs);

        // Strona master: połączenie do silnika.
        using var drive = new DriveLink(_opt, logger);

        PlcOutputs last = default;
        bool lastWriteOk = true;

        var clock = Stopwatch.StartNew();
        var previous = clock.Elapsed;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_opt.CycleMs));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var now = clock.Elapsed;
                var dt = now - previous;
                previous = now;

                // 1. Odczyt wejść: polecenia i ustawienia od bramy, pomiary z silnika.
                CommandBits cmd = memory.TakeCommands();
                (ushort speedSetpoint, ushort heartbeat) = memory.ReadSettings();
                bool readOk = drive.TryReadMeasurements(out int rpm, out int tempTenths);

                // 2. Logika.
                PlcOutputs o = logic.Step(
                    new PlcInputs(cmd.Start, cmd.Stop, cmd.Estop, cmd.Reset,
                        heartbeat, speedSetpoint,
                        DriveOk: readOk && lastWriteOk, rpm, tempTenths),
                    dt);

                // 3. Zapis wyjść: do silnika i stan dla bramy.
                lastWriteOk = drive.TryWriteOutputs(o.Run, o.Brake, o.SpeedRef);
                memory.Publish(o);

                LogChanges(cmd, last, o);
                last = o;
            }
        }
        catch (OperationCanceledException)
        {
            // normalne zamknięcie
        }
        finally
        {
            listener.Stop();
        }
    }

    private void LogChanges(CommandBits cmd, PlcOutputs before, PlcOutputs after)
    {
        if (cmd.Any)
        {
            logger.LogInformation("Polecenia w cyklu:{Start}{Stop}{Estop}{Reset}",
                cmd.Start ? " START" : "", cmd.Stop ? " STOP" : "",
                cmd.Estop ? " ESTOP" : "", cmd.Reset ? " RESET" : "");
        }
        if (after.Alarm != before.Alarm)
        {
            if (after.Alarm == AlarmCode.None)
                logger.LogInformation("Alarm skasowany");
            else
                logger.LogWarning("ALARM: {Alarm}", Describe(after.Alarm));
        }
        if (after.State != before.State || after.Run != before.Run || after.Brake != before.Brake)
        {
            logger.LogInformation("Stan: {State} | RUN={Run} BRAKE={Brake} SPEED_REF={SpeedRef} | {Rpm} obr/min",
                after.State, after.Run ? 1 : 0, after.Brake ? 1 : 0, after.SpeedRef, after.Rpm);
        }
    }

    private static string Describe(AlarmCode alarm) => alarm switch
    {
        AlarmCode.Overheat => "przegrzanie silnika",
        AlarmCode.HmiCommLost => "brak komunikacji z HMI",
        AlarmCode.DriveCommLost => "brak komunikacji z napędem",
        _ => alarm.ToString()
    };
}
