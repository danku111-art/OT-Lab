using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using NModbus;
using OtLab.Shared;

namespace OtLab.Motor;

/// <summary>
/// Cienka warstwa Modbus wokół modelu: NModbus sam przyjmuje ramki i wpisuje
/// wartości do tabel, a my co 50 ms czytamy z tabel wejścia modelu i wpisujemy wyniki.
/// </summary>
public sealed class MotorService(
    MotorModel model,
    IOptions<MotorOptions> options,
    ILogger<MotorService> logger) : BackgroundService
{
    private readonly MotorOptions _opt = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Parse(_opt.ListenAddress), _opt.ListenPort);
        listener.Start();

        var factory = new ModbusFactory();
        var network = factory.CreateSlaveNetwork(listener);
        var slave = factory.CreateSlave(_opt.UnitId); // domyślne tabele NModbus (mapa B)
        network.AddSlave(slave);
        _ = network.ListenAsync(stoppingToken);

        logger.LogInformation("Silnik nasłuchuje Modbus TCP na {Address}:{Port} (mapa B)",
            _opt.ListenAddress, _opt.ListenPort);
        if (!Console.IsInputRedirected)
            logger.LogInformation("Klawisze: P = przełącz „przegrzej”, Ctrl+C = koniec");

        var store = slave.DataStore;
        bool lastRun = false, lastBrake = false;
        ushort lastSpeedRef = 0;

        var clock = Stopwatch.StartNew();
        var previous = clock.Elapsed;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_opt.StepMs));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                // Rzeczywisty czas od poprzedniego kroku (system nie gwarantuje równych 50 ms).
                var now = clock.Elapsed;
                var dt = now - previous;
                previous = now;

                HandleKeyboard();

                // 1. Wejścia modelu z tabel Modbus (zapisał je tam PLC).
                bool[] coils = store.CoilDiscretes.ReadPoints(ModbusMapB.Run, ModbusMapB.CoilCount);
                bool run = coils[ModbusMapB.Run];
                bool brake = coils[ModbusMapB.Brake];
                ushort speedRef = store.HoldingRegisters.ReadPoints(ModbusMapB.SpeedRef, 1)[0];

                // 2. Fizyka.
                model.Update(run, brake, speedRef, dt);

                // 3. Wyniki do tabel Modbus (odczyta je stamtąd PLC).
                store.InputRegisters.WritePoints(ModbusMapB.Rpm,
                    [(ushort)model.Rpm, (ushort)model.TempTenths]);

                if (run != lastRun || brake != lastBrake || speedRef != lastSpeedRef)
                {
                    logger.LogInformation(
                        "Wejścia od PLC: RUN={Run} BRAKE={Brake} SPEED_REF={SpeedRef} | {Rpm} obr/min, {Temp:F1} °C",
                        run ? 1 : 0, brake ? 1 : 0, speedRef, model.Rpm,
                        model.TempTenths / (double)NetworkDefaults.TempScale);
                    (lastRun, lastBrake, lastSpeedRef) = (run, brake, speedRef);
                }
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

    private void HandleKeyboard()
    {
        if (Console.IsInputRedirected) return;
        while (Console.KeyAvailable)
        {
            if (Console.ReadKey(intercept: true).Key == ConsoleKey.P)
            {
                model.Overheat = !model.Overheat;
                logger.LogWarning("Przełącznik „przegrzej”: {State}", model.Overheat ? "WŁĄCZONY" : "wyłączony");
            }
        }
    }
}
