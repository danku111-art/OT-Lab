using OtLab.Plc;

// soft-PLC: slave Modbus TCP dla bramy (mapa A, port 5020), master dla silnika (mapa B, port 5021).

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss.fff ";
});
builder.Services.Configure<PlcOptions>(builder.Configuration.GetSection("Plc"));
builder.Services.AddSingleton<PlcMemory>();
builder.Services.AddSingleton<PlcLogic>();
builder.Services.AddHostedService<PlcService>();

builder.Build().Run();
