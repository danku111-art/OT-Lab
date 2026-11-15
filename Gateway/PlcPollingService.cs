using Microsoft.Extensions.Options;
using OtLab.Shared;

namespace OtLab.Gateway;

/// <summary>
/// Dwie pętle bramy: odpytywanie stanu PLC co 200 ms (i rozsyłanie go do HMI)
/// oraz heartbeat co 500 ms, po którym PLC poznaje, że brama żyje.
/// </summary>
public sealed class PlcPollingService(
    PlcLink plc,
    HmiHub hub,
    IOptions<GatewayOptions> options) : BackgroundService
{
    private readonly GatewayOptions _opt = options.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(PollLoopAsync(stoppingToken), HeartbeatLoopAsync(stoppingToken));

    private async Task PollLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_opt.PollMs));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                PlcSnapshot? s = plc.TryReadStatus();
                hub.LastSnapshot = s;

                if (s is null)
                {
                    hub.Broadcast(new LinkMessage("link", PlcConnected: false));
                    continue;
                }

                hub.Broadcast(new StatusMessage(
                    "status",
                    s.State,
                    s.Rpm,
                    s.TempTenths / (double)NetworkDefaults.TempScale,
                    s.Alarm,
                    s.SpeedSetpoint,
                    s.Running,
                    s.EstopLatched,
                    s.Fault));
            }
        }
        catch (OperationCanceledException)
        {
            // normalne zamknięcie
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        ushort counter = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_opt.HeartbeatMs));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                counter++; // ushort sam się przewija 65535 -> 0; PLC patrzy tylko, czy wartość się zmienia
                plc.TryWriteHeartbeat(counter);
            }
        }
        catch (OperationCanceledException)
        {
            // normalne zamknięcie
        }
    }
}
