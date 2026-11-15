using OtLab.Plc;
using OtLab.Shared;

namespace OtLab.Tests;

/// <summary>
/// Testy reguł sterowania z rozdziału 8. Każdy test to seria cykli po 100 ms
/// z ręcznie podanymi wejściami – bez sieci, bez zegara, bez czekania.
/// </summary>
public class PlcLogicTests
{
    private static readonly TimeSpan Cycle = TimeSpan.FromMilliseconds(100);

    private readonly PlcLogic _plc = new();
    private ushort _heartbeat;

    /// <summary>Jeden cykl z żywą bramą (heartbeat zmienia się w każdym cyklu).</summary>
    private PlcOutputs Step(
        bool start = false, bool stop = false, bool estop = false, bool reset = false,
        bool driveOk = true, int rpm = 0, int temp = 220, ushort setpoint = 1500,
        bool heartbeatAlive = true)
    {
        if (heartbeatAlive) _heartbeat++;
        return _plc.Step(
            new PlcInputs(start, stop, estop, reset, _heartbeat, setpoint, driveOk, rpm, temp), Cycle);
    }

    private PlcOutputs Steps(int count, bool driveOk = true, int rpm = 0, int temp = 220, bool heartbeatAlive = true)
    {
        PlcOutputs o = default;
        for (int i = 0; i < count; i++)
            o = Step(driveOk: driveOk, rpm: rpm, temp: temp, heartbeatAlive: heartbeatAlive);
        return o;
    }

    [Fact]
    public void Po_uruchomieniu_PLC_stoi_z_RUN_0()
    {
        PlcOutputs o = Step();

        Assert.False(o.Run);
        Assert.False(o.Brake);
        Assert.Equal(PlcState.Stopped, o.State);
        Assert.Equal(AlarmCode.None, o.Alarm);
        Assert.Equal(1500, o.SpeedRef);
    }

    [Fact]
    public void Start_ustawia_RUN_i_stan_rozpedza_sie_a_potem_pracuje()
    {
        PlcOutputs o = Step(start: true);
        Assert.True(o.Run);
        Assert.Equal(PlcState.Starting, o.State);

        o = Step(rpm: 800);
        Assert.Equal(PlcState.Starting, o.State);

        o = Step(rpm: 1500);
        Assert.Equal(PlcState.Running, o.State);
    }

    [Fact]
    public void Stop_kasuje_RUN_stan_hamuje_a_potem_stoi()
    {
        Step(start: true);
        Step(rpm: 1500);

        PlcOutputs o = Step(stop: true, rpm: 1500);
        Assert.False(o.Run);
        Assert.False(o.Brake); // zwykły stop to wybieg, bez hamulca
        Assert.Equal(PlcState.Braking, o.State);

        o = Step(rpm: 0);
        Assert.Equal(PlcState.Stopped, o.State);

        // Start działa od razu po zatrzymaniu.
        Assert.True(Step(start: true).Run);
    }

    [Fact]
    public void Start_i_Stop_w_jednym_cyklu_wygrywa_Stop()
    {
        Assert.False(Step(start: true, stop: true).Run);

        // Także gdy silnik już pracuje.
        Step(start: true);
        Assert.False(Step(start: true, stop: true, rpm: 1500).Run);
    }

    [Fact]
    public void Estop_kasuje_RUN_wlacza_hamulec_i_sie_zatrzaskuje()
    {
        Step(start: true);

        PlcOutputs o = Step(estop: true, rpm: 1500);
        Assert.False(o.Run);
        Assert.True(o.Brake);
        Assert.True(o.EstopLatched);
        Assert.Equal(PlcState.Estop, o.State);

        // Zatrzask trwa, choć polecenie e-stop było tylko w jednym cyklu.
        o = Steps(20);
        Assert.True(o.EstopLatched);
        Assert.True(o.Brake);
    }

    [Fact]
    public void Po_estopie_Start_nic_nie_robi()
    {
        Step(estop: true);

        PlcOutputs o = Step(start: true);

        Assert.False(o.Run);
        Assert.Equal(PlcState.Estop, o.State);
    }

    [Fact]
    public void Estop_wygrywa_ze_Startem_w_tym_samym_cyklu()
    {
        PlcOutputs o = Step(start: true, estop: true);

        Assert.False(o.Run);
        Assert.True(o.EstopLatched);
    }

    [Fact]
    public void Reset_kasuje_estop_ale_nie_uruchamia_silnika()
    {
        Step(start: true);
        Step(estop: true, rpm: 1500);

        PlcOutputs o = Step(reset: true);
        Assert.False(o.EstopLatched);
        Assert.False(o.Brake);
        Assert.False(o.Run);
        Assert.Equal(PlcState.Stopped, o.State);

        // Silnik nie rusza sam także w kolejnych cyklach.
        Assert.False(Steps(10).Run);
    }

    [Fact]
    public void Reset_i_Start_w_jednym_cyklu_nie_uruchamia_silnika()
    {
        Step(estop: true);

        PlcOutputs o = Step(reset: true, start: true);
        Assert.False(o.EstopLatched);
        Assert.False(o.Run);

        // Start razem z resetem nie rusza silnika także wtedy, gdy nie było żadnej blokady.
        Assert.False(new PlcLogic().Step(
            PlcInputs.Idle(1) with { CmdStart = true, CmdReset = true }, Cycle).Run);
    }

    [Fact]
    public void Reset_i_Estop_w_jednym_cyklu_wygrywa_Estop()
    {
        Step(estop: true);

        PlcOutputs o = Step(reset: true, estop: true);

        Assert.True(o.EstopLatched);
        Assert.True(o.Brake);
    }

    [Fact]
    public void Reset_podczas_pracy_bez_blokad_nie_zatrzymuje_silnika()
    {
        Step(start: true);

        Assert.True(Step(reset: true, rpm: 1500).Run);
    }

    // ----- Watchdog bramy (heartbeat) -----

    [Fact]
    public void Brak_zmiany_heartbeatu_przez_2_s_zatrzymuje_silnik()
    {
        Step(start: true);

        // 1,9 s ciszy: jeszcze pracuje.
        PlcOutputs o = Steps(19, rpm: 1500, heartbeatAlive: false);
        Assert.True(o.Run);
        Assert.Equal(AlarmCode.None, o.Alarm);

        // 2,0 s ciszy: awaria.
        o = Step(rpm: 1500, heartbeatAlive: false);
        Assert.False(o.Run);
        Assert.Equal(AlarmCode.HmiCommLost, o.Alarm);
        Assert.Equal(PlcState.Fault, o.State);
    }

    [Fact]
    public void Brak_pierwszego_heartbeatu_przez_5_s_od_startu_PLC_to_awaria()
    {
        Assert.Equal(AlarmCode.None, Steps(49, heartbeatAlive: false).Alarm);
        Assert.Equal(AlarmCode.HmiCommLost, Step(heartbeatAlive: false).Alarm);
    }

    [Fact]
    public void Reset_awarii_HMI_dziala_dopiero_gdy_heartbeat_wrocil()
    {
        Steps(50, heartbeatAlive: false);

        // Brama dalej milczy: reset nic nie daje.
        Assert.Equal(AlarmCode.HmiCommLost, Step(reset: true, heartbeatAlive: false).Alarm);

        // Brama wróciła, ale alarm sam nie znika – to zatrzask.
        PlcOutputs o = Steps(5);
        Assert.Equal(AlarmCode.HmiCommLost, o.Alarm);
        Assert.False(Step(start: true).Run);

        // Dopiero reset go kasuje.
        o = Step(reset: true);
        Assert.Equal(AlarmCode.None, o.Alarm);
        Assert.Equal(PlcState.Stopped, o.State);
        Assert.True(Step(start: true).Run);
    }

    // ----- Watchdog silnika -----

    [Fact]
    public void Trzy_cykle_bez_odpowiedzi_silnika_to_awaria()
    {
        Step(start: true);

        Assert.True(Step(driveOk: false).Run);
        Assert.True(Step(driveOk: false).Run);

        PlcOutputs o = Step(driveOk: false);
        Assert.False(o.Run);
        Assert.Equal(AlarmCode.DriveCommLost, o.Alarm);
        Assert.Equal(PlcState.Fault, o.State);
    }

    [Fact]
    public void Pojedyncze_zgubione_odpowiedzi_silnika_nie_powoduja_awarii()
    {
        Step(start: true);
        for (int i = 0; i < 10; i++)
        {
            Step(driveOk: false);
            Step(driveOk: false);
            Step(driveOk: true, rpm: 1500);
        }

        PlcOutputs o = Step(rpm: 1500);
        Assert.True(o.Run);
        Assert.Equal(AlarmCode.None, o.Alarm);
    }

    [Fact]
    public void Reset_awarii_napedu_dziala_dopiero_gdy_silnik_odpowiada()
    {
        Steps(3, driveOk: false);

        Assert.Equal(AlarmCode.DriveCommLost, Step(reset: true, driveOk: false).Alarm);

        // Silnik wrócił: Start dalej zablokowany, aż do resetu.
        Assert.False(Step(start: true).Run);
        Assert.Equal(AlarmCode.None, Step(reset: true).Alarm);
        Assert.True(Step(start: true).Run);
    }

    // ----- Przegrzanie -----

    [Fact]
    public void Temperatura_powyzej_80_stopni_zatrzymuje_silnik()
    {
        Step(start: true);

        Assert.True(Step(rpm: 1500, temp: 800).Run); // równo 80,0 °C to jeszcze nie alarm

        PlcOutputs o = Step(rpm: 1500, temp: 801);
        Assert.False(o.Run);
        Assert.Equal(AlarmCode.Overheat, o.Alarm);
    }

    [Fact]
    public void Reset_przegrzania_dziala_dopiero_ponizej_60_stopni()
    {
        Step(temp: 850);

        Assert.Equal(AlarmCode.Overheat, Step(reset: true, temp: 700).Alarm);
        Assert.Equal(AlarmCode.Overheat, Step(reset: true, temp: 600).Alarm);
        Assert.Equal(AlarmCode.None, Step(reset: true, temp: 599).Alarm);
    }

    // ----- Stany i nastawa -----

    [Fact]
    public void Kilka_awarii_naraz_ALARM_CODE_pokazuje_najwazniejsza()
    {
        // brak heartbeatu przez 5 s i jednocześnie brak silnika
        PlcOutputs o = Steps(50, driveOk: false, heartbeatAlive: false);
        Assert.Equal(AlarmCode.DriveCommLost, o.Alarm);

        // Silnik i brama wróciły; reset kasuje obie awarie.
        Steps(3);
        Assert.Equal(AlarmCode.None, Step(reset: true).Alarm);
    }

    [Fact]
    public void Estop_przy_aktywnej_awarii_stan_to_estop_a_alarm_zostaje()
    {
        Step(temp: 850);

        PlcOutputs o = Step(estop: true, temp: 850);

        Assert.Equal(PlcState.Estop, o.State);
        Assert.Equal(AlarmCode.Overheat, o.Alarm);
        Assert.True(o.Fault);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1200, 1200)]
    [InlineData(3000, 3000)]
    [InlineData(5000, 3000)]
    public void SPEED_REF_to_nastawa_ograniczona_do_3000(int setpoint, int expected)
    {
        Assert.Equal(expected, Step(setpoint: (ushort)setpoint).SpeedRef);
    }
}
