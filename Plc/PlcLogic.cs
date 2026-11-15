using OtLab.Shared;

namespace OtLab.Plc;

/// <summary>Wejścia jednego cyklu PLC.</summary>
public readonly record struct PlcInputs(
    bool CmdStart,
    bool CmdStop,
    bool CmdEstop,
    bool CmdReset,
    ushort Heartbeat,
    ushort SpeedSetpoint,
    bool DriveOk,   // czy w tym cyklu silnik odpowiedział
    int Rpm,        // ważne tylko, gdy DriveOk
    int TempTenths) // ważne tylko, gdy DriveOk
{
    /// <summary>Cykl bez poleceń, z działającym silnikiem i domyślną nastawą.</summary>
    public static PlcInputs Idle(ushort heartbeat, int rpm = 0, int tempTenths = 220) =>
        new(false, false, false, false, heartbeat, NetworkDefaults.DefaultSpeedRpm, true, rpm, tempTenths);
}

/// <summary>Wyjścia jednego cyklu PLC: sygnały do silnika i stan dla bramy.</summary>
public readonly record struct PlcOutputs(
    bool Run,
    bool Brake,
    ushort SpeedRef,
    PlcState State,
    AlarmCode Alarm,
    bool EstopLatched,
    int Rpm,
    int TempTenths)
{
    public bool Fault => Alarm != AlarmCode.None;
}

/// <summary>
/// Cała logika sterowania soft-PLC. Klasa nie zna Modbusa ani zegara:
/// dostaje wejścia i czas od poprzedniego cyklu, zwraca wyjścia.
/// Dzięki temu każdą regułę można sprawdzić testem jednostkowym.
/// </summary>
public sealed class PlcLogic
{
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan FirstHeartbeatTimeout = TimeSpan.FromSeconds(5);
    public const int DriveFailLimit = 3;         // tyle nieudanych cykli z rzędu = awaria
    public const int OverheatTripTenths = 800;   // powyżej 80 °C: awaria
    public const int OverheatResetTenths = 600;  // reset przegrzania dopiero poniżej 60 °C
    public const int AtSpeedToleranceRpm = 20;   // „pracuje”, gdy do nastawy brakuje najwyżej tyle

    // Zatrzaski: raz ustawione trwają, aż skasuje je RESET (i tylko gdy przyczyna ustąpiła).
    private bool _estopLatched;
    private bool _faultDrive;
    private bool _faultOverheat;
    private bool _faultHmi;

    private bool _run;

    // Watchdog bramy.
    private ushort _lastHeartbeat;
    private bool _heartbeatSeen;
    private TimeSpan _sinceHeartbeat;

    // Watchdog silnika i ostatnie poprawne pomiary.
    private int _driveFails;
    private int _rpm;
    private int _tempTenths = 220;

    public PlcOutputs Step(PlcInputs input, TimeSpan dt)
    {
        // --- Obserwacja: watchdogi i pomiary ---------------------------------

        _sinceHeartbeat += dt;
        if (input.Heartbeat != _lastHeartbeat)
        {
            _lastHeartbeat = input.Heartbeat;
            _heartbeatSeen = true;
            _sinceHeartbeat = TimeSpan.Zero;
        }
        bool heartbeatLost = _sinceHeartbeat >= (_heartbeatSeen ? HeartbeatTimeout : FirstHeartbeatTimeout);
        bool heartbeatAlive = _heartbeatSeen && !heartbeatLost;

        if (input.DriveOk)
        {
            _driveFails = 0;
            _rpm = input.Rpm;
            _tempTenths = input.TempTenths;
        }
        else if (_driveFails < DriveFailLimit)
        {
            _driveFails++;
        }
        bool driveLost = _driveFails >= DriveFailLimit;

        // --- Reguły w kolejności ważności ------------------------------------

        // 1. E-stop: zatrzask, RUN = 0, BRAKE = 1.
        if (input.CmdEstop) _estopLatched = true;

        // 2. Brak odpowiedzi silnika przez 3 kolejne cykle.
        if (driveLost) _faultDrive = true;

        // 3. Przegrzanie.
        if (_tempTenths > OverheatTripTenths) _faultOverheat = true;

        // 4. Brak heartbeatu od bramy.
        if (heartbeatLost) _faultHmi = true;

        // 7. RESET kasuje tylko te zatrzaski, których przyczyna ustąpiła.
        //    Ma najniższy priorytet: przegrywa z e-stopem z tego samego cyklu
        //    i nigdy nie uruchamia silnika (patrz warunek przy START niżej).
        if (input.CmdReset)
        {
            if (!input.CmdEstop) _estopLatched = false;
            if (_driveFails == 0) _faultDrive = false;
            if (_tempTenths < OverheatResetTenths) _faultOverheat = false;
            if (heartbeatAlive) _faultHmi = false;
        }

        bool blocked = _estopLatched || _faultDrive || _faultOverheat || _faultHmi;

        if (blocked || input.CmdStop)
        {
            // 5. STOP (i każda blokada) wygrywa ze START z tego samego cyklu.
            _run = false;
        }
        else if (input.CmdStart && !input.CmdReset)
        {
            // 6. START tylko bez blokad i bez STOP/RESET w tym samym cyklu.
            _run = true;
        }

        // --- Wyjścia ----------------------------------------------------------

        ushort speedRef = Math.Min(input.SpeedSetpoint, NetworkDefaults.MaxSpeedRpm);

        // Kolejność jak w regułach: napęd, przegrzanie, HMI.
        AlarmCode alarm =
            _faultDrive ? AlarmCode.DriveCommLost :
            _faultOverheat ? AlarmCode.Overheat :
            _faultHmi ? AlarmCode.HmiCommLost :
            AlarmCode.None;

        PlcState state =
            _estopLatched ? PlcState.Estop :
            alarm != AlarmCode.None ? PlcState.Fault :
            _run ? (_rpm < speedRef - AtSpeedToleranceRpm ? PlcState.Starting : PlcState.Running) :
            _rpm > 0 ? PlcState.Braking :
            PlcState.Stopped;

        return new PlcOutputs(
            Run: _run,
            Brake: _estopLatched, // hamulec trzyma tak długo, jak zatrzask e-stopu
            SpeedRef: speedRef,
            State: state,
            Alarm: alarm,
            EstopLatched: _estopLatched,
            Rpm: _rpm,
            TempTenths: _tempTenths);
    }
}
