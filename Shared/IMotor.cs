namespace OtLab.Shared;

/// <summary>Model silnika. Tylko deklaracja; implementacja jest w projekcie Motor.</summary>
public interface IMotor
{
    /// <summary>Przelicza model o krok czasu dt przy podanych sygnałach sterujących.</summary>
    void Update(bool run, bool brake, int speedRef, TimeSpan dt);

    /// <summary>Obroty w obr/min.</summary>
    int Rpm { get; }

    /// <summary>Temperatura w 0,1 °C.</summary>
    int TempTenths { get; }
}
