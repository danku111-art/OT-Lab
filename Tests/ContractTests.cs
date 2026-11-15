using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OtLab.Gateway;
using OtLab.Shared;

namespace OtLab.Tests;

/// <summary>Kontrakt JSON między bramą a HMI (docs/websocket.md) i dziennik poleceń.</summary>
public class ContractTests
{
    [Fact]
    public void Status_ma_dokladnie_taki_JSON_jak_w_dokumentacji()
    {
        var status = new StatusMessage("status", PlcState.Running, 1500, 42.5, AlarmCode.None,
            SpeedSetpoint: 1500, Running: true, EstopLatched: false, Fault: false);

        string json = JsonSerializer.Serialize(status, JsonContract.Options);

        Assert.Equal(
            """{"type":"status","state":"running","rpm":1500,"temperatureCelsius":42.5,"alarm":"none","speedSetpoint":1500,"running":true,"estopLatched":false,"fault":false}""",
            json);
    }

    [Theory]
    [InlineData(AlarmCode.HmiCommLost, "\"hmiCommLost\"")]
    [InlineData(AlarmCode.DriveCommLost, "\"driveCommLost\"")]
    [InlineData(AlarmCode.Overheat, "\"overheat\"")]
    public void Alarmy_sa_pisane_camelCase(AlarmCode alarm, string expected)
    {
        Assert.Equal(expected, JsonSerializer.Serialize(alarm, JsonContract.Options));
    }

    [Fact]
    public void Polecenie_z_HMI_da_sie_odczytac()
    {
        var start = JsonSerializer.Deserialize<CommandMessage>("""{ "cmd": "start" }""", JsonContract.Options);
        var speed = JsonSerializer.Deserialize<CommandMessage>("""{"cmd":"setSpeed","value":1200}""", JsonContract.Options);

        Assert.Equal(new CommandMessage("start"), start);
        Assert.Equal(new CommandMessage("setSpeed", 1200), speed);
    }

    [Fact]
    public void Wpis_dziennika_pomija_puste_pola()
    {
        var entry = new CommandLogEntry(
            new DateTimeOffset(2026, 11, 16, 9, 30, 0, TimeSpan.FromHours(1)),
            "start", null, "127.0.0.1:50000", CommandResult.Sent, PlcState.Stopped, null);

        string json = JsonSerializer.Serialize(entry, JsonContract.Options);

        Assert.Equal(
            """{"time":"2026-11-16T09:30:00+01:00","cmd":"start","source":"127.0.0.1:50000","result":"sent","stateBefore":"stopped"}""",
            json);
    }

    [Fact]
    public void Dziennik_zapisuje_do_pliku_i_pamieta_tylko_ostatnie_wpisy()
    {
        string path = Path.Combine(Path.GetTempPath(), $"otlab-test-{Guid.NewGuid():N}", "commands.jsonl");
        try
        {
            var log = new CommandLog(path, capacity: 100, NullLogger<CommandLog>.Instance);
            for (int i = 0; i < 250; i++) log.Append(Entry(i));

            // W pliku jest wszystko, na ekran idzie ostatnie 100.
            Assert.Equal(250, File.ReadAllLines(path).Length);
            Assert.Equal(100, log.Recent().Count);
            Assert.Equal(150, log.Recent()[0].Value);
            Assert.Equal(249, log.Recent()[^1].Value);

            // Po restarcie bramy historia wraca z pliku.
            var reopened = new CommandLog(path, capacity: 100, NullLogger<CommandLog>.Instance);
            Assert.Equal(100, reopened.Recent().Count);
            Assert.Equal(249, reopened.Recent()[^1].Value);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Dziennik_pomija_uszkodzone_linie_pliku()
    {
        string path = Path.Combine(Path.GetTempPath(), $"otlab-test-{Guid.NewGuid():N}", "commands.jsonl");
        try
        {
            var log = new CommandLog(path, capacity: 100, NullLogger<CommandLog>.Instance);
            log.Append(Entry(1));
            File.AppendAllText(path, "{\"time\":\"2026-11-16T09:3" + Environment.NewLine); // urwana linia
            log.Append(Entry(2));

            var reopened = new CommandLog(path, capacity: 100, NullLogger<CommandLog>.Instance);

            Assert.Equal([1, 2], reopened.Recent().Select(e => e.Value));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    private static CommandLogEntry Entry(int n) => new(
        DateTimeOffset.Now, "setSpeed", n, "127.0.0.1:50000", CommandResult.Sent, PlcState.Stopped, null);
}
