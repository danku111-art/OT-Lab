using NModbus;
using OtLab.Shared;

namespace OtLab.Plc;

/// <summary>Polecenia odebrane od bramy w jednym cyklu.</summary>
public readonly record struct CommandBits(bool Start, bool Stop, bool Estop, bool Reset)
{
    public bool Any => Start || Stop || Estop || Reset;
}

/// <summary>
/// Pamięć PLC widoczna przez Modbus (mapa A).
/// NModbus zapisuje tu polecenia bramy w swoim wątku, a cykl PLC czyta je w innym,
/// dlatego wszystkie tabele dzielą jeden lock. Tabele mają dokładnie tyle komórek,
/// ile jest w mapie – zapytanie o adres spoza mapy dostaje wyjątek Modbus
/// „Illegal Data Address”, tak jak w prawdziwym sterowniku.
/// </summary>
public sealed class PlcMemory : ISlaveDataStore
{
    private readonly object _gate = new();
    private readonly Table<bool> _coils;
    private readonly Table<bool> _discreteInputs;
    private readonly Table<ushort> _holdingRegisters;
    private readonly Table<ushort> _inputRegisters;

    public PlcMemory()
    {
        _coils = new Table<bool>(_gate, ModbusMapA.CoilCount);
        _discreteInputs = new Table<bool>(_gate, ModbusMapA.DiscreteInputCount);
        _holdingRegisters = new Table<ushort>(_gate, ModbusMapA.HoldingRegisterCount);
        _inputRegisters = new Table<ushort>(_gate, ModbusMapA.InputRegisterCount);

        // Po uruchomieniu PLC: nastawa domyślna, stan „stoi”.
        _holdingRegisters.Cells[ModbusMapA.SpeedSetpoint] = NetworkDefaults.DefaultSpeedRpm;
        _inputRegisters.Cells[ModbusMapA.Temp] = 220;
    }

    // Nazwy z NModbus: CoilDiscretes = coils, CoilInputs = discrete inputs.
    public IPointSource<bool> CoilDiscretes => _coils;
    public IPointSource<bool> CoilInputs => _discreteInputs;
    public IPointSource<ushort> HoldingRegisters => _holdingRegisters;
    public IPointSource<ushort> InputRegisters => _inputRegisters;

    /// <summary>
    /// Odczytuje polecenia i od razu je kasuje – w jednym locku, żeby nie zgubić
    /// polecenia, które przyszłoby dokładnie między odczytem a wyzerowaniem.
    /// </summary>
    public CommandBits TakeCommands()
    {
        lock (_gate)
        {
            bool[] c = _coils.Cells;
            var bits = new CommandBits(
                c[ModbusMapA.CmdStart], c[ModbusMapA.CmdStop],
                c[ModbusMapA.CmdEstop], c[ModbusMapA.CmdReset]);
            Array.Clear(c);
            return bits;
        }
    }

    /// <summary>Odczytuje ustawienia od bramy; nastawę prędkości ogranicza do zakresu 0–3000.</summary>
    public (ushort SpeedSetpoint, ushort Heartbeat) ReadSettings()
    {
        lock (_gate)
        {
            ushort[] h = _holdingRegisters.Cells;
            if (h[ModbusMapA.SpeedSetpoint] > NetworkDefaults.MaxSpeedRpm)
                h[ModbusMapA.SpeedSetpoint] = NetworkDefaults.MaxSpeedRpm;
            return (h[ModbusMapA.SpeedSetpoint], h[ModbusMapA.Heartbeat]);
        }
    }

    /// <summary>Publikuje stan dla bramy – wszystko naraz, żeby brama nie zobaczyła połowy cyklu.</summary>
    public void Publish(PlcOutputs o)
    {
        lock (_gate)
        {
            bool[] d = _discreteInputs.Cells;
            d[ModbusMapA.StRunning] = o.Run;
            d[ModbusMapA.StEstop] = o.EstopLatched;
            d[ModbusMapA.StFault] = o.Fault;

            ushort[] r = _inputRegisters.Cells;
            r[ModbusMapA.State] = (ushort)o.State;
            r[ModbusMapA.Rpm] = (ushort)o.Rpm;
            r[ModbusMapA.Temp] = (ushort)o.TempTenths;
            r[ModbusMapA.AlarmCode] = (ushort)o.Alarm;
        }
    }

    /// <summary>Jedna tabela Modbus o stałym rozmiarze, chroniona wspólnym lockiem.</summary>
    private sealed class Table<T>(object gate, int size) : IPointSource<T>
    {
        public T[] Cells { get; } = new T[size];

        public T[] ReadPoints(ushort startAddress, ushort numberOfPoints)
        {
            CheckRange(startAddress, numberOfPoints);
            lock (gate)
            {
                return Cells.AsSpan(startAddress, numberOfPoints).ToArray();
            }
        }

        public void WritePoints(ushort startAddress, T[] points)
        {
            CheckRange(startAddress, points.Length);
            lock (gate)
            {
                points.CopyTo(Cells, startAddress);
            }
        }

        private void CheckRange(int start, int count)
        {
            if (start + count > Cells.Length)
                throw new InvalidModbusRequestException(SlaveExceptionCodes.IllegalDataAddress);
        }
    }
}
