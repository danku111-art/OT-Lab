using OtLab.Motor;

namespace OtLab.Tests;

public class MotorModelTests
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(50);

    private static void Run(MotorModel motor, double seconds, bool run, bool brake = false, int speedRef = 1500)
    {
        int steps = (int)Math.Round(seconds / Step.TotalSeconds);
        for (int i = 0; i < steps; i++) motor.Update(run, brake, speedRef, Step);
    }

    [Fact]
    public void Na_poczatku_stoi_i_ma_temperature_otoczenia()
    {
        var motor = new MotorModel();

        Assert.Equal(0, motor.Rpm);
        Assert.Equal(220, motor.TempTenths);
    }

    [Fact]
    public void Rozpedza_sie_1000_obr_min_na_sekunde()
    {
        var motor = new MotorModel();

        Run(motor, 1.0, run: true);
        Assert.Equal(1000, motor.Rpm);

        Run(motor, 0.5, run: true);
        Assert.Equal(1500, motor.Rpm);
    }

    [Fact]
    public void Nie_przekracza_zadanej_predkosci()
    {
        var motor = new MotorModel();

        Run(motor, 10, run: true, speedRef: 1500);

        Assert.Equal(1500, motor.Rpm);
    }

    [Fact]
    public void SPEED_REF_powyzej_3000_jest_ograniczany()
    {
        var motor = new MotorModel();

        Run(motor, 10, run: true, speedRef: 9000);

        Assert.Equal(3000, motor.Rpm);
    }

    [Fact]
    public void Wybieg_500_obr_min_na_sekunde()
    {
        var motor = new MotorModel();
        Run(motor, 2, run: true);

        Run(motor, 1.0, run: false);
        Assert.Equal(1000, motor.Rpm);

        Run(motor, 5, run: false);
        Assert.Equal(0, motor.Rpm);
    }

    [Fact]
    public void Hamulec_2000_obr_min_na_sekunde_i_wygrywa_z_RUN()
    {
        var motor = new MotorModel();
        Run(motor, 2, run: true);

        Run(motor, 0.5, run: true, brake: true);
        Assert.Equal(500, motor.Rpm);

        Run(motor, 0.5, run: true, brake: true);
        Assert.Equal(0, motor.Rpm);
    }

    [Fact]
    public void Po_zmniejszeniu_nastawy_zwalnia_do_nowej_predkosci()
    {
        var motor = new MotorModel();
        Run(motor, 2, run: true, speedRef: 1500);

        Run(motor, 5, run: true, speedRef: 600);

        Assert.Equal(600, motor.Rpm);
    }

    [Fact]
    public void Nagrzewa_sie_przy_pracy_i_stygnie_po_zatrzymaniu()
    {
        var motor = new MotorModel();

        Run(motor, 120, run: true, speedRef: 1500);
        Assert.InRange(motor.TempTenths, 410, 422); // dąży do 22 + 40 * 1500/3000 = 42 °C

        Run(motor, 300, run: false);
        Assert.InRange(motor.TempTenths, 220, 222);
    }

    [Fact]
    public void Normalna_praca_nawet_na_3000_obr_min_nie_przegrzewa()
    {
        var motor = new MotorModel();

        Run(motor, 600, run: true, speedRef: 3000);

        Assert.True(motor.TempTenths < 800);
    }

    [Fact]
    public void Przelacznik_przegrzej_podnosi_temperature_ponad_80_stopni()
    {
        var motor = new MotorModel { Overheat = true };

        Run(motor, 60, run: false);
        Assert.True(motor.TempTenths > 800);

        motor.Overheat = false;
        Run(motor, 60, run: false);
        Assert.True(motor.TempTenths < 600);
    }
}
