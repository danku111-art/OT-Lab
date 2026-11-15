using OtLab.Motor;

// Symulator silnika: osobny proces, slave Modbus TCP (mapa B, domyślnie port 5021).

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss.fff ";
});
builder.Services.Configure<MotorOptions>(builder.Configuration.GetSection("Motor"));
builder.Services.AddSingleton<MotorModel>();
builder.Services.AddHostedService<MotorService>();

builder.Build().Run();
