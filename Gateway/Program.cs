using OtLab.Gateway;

// Brama HMI: serwuje stronę HMI, rozmawia z przeglądarką przez WebSocket (JSON)
// i z PLC przez Modbus TCP (mapa A). Sama niczego nie decyduje.

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss.fff ";
});
builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection("Gateway"));
builder.Services.AddSingleton<PlcLink>();
builder.Services.AddSingleton<CommandLog>();
builder.Services.AddSingleton<HmiHub>();
builder.Services.AddHostedService<PlcPollingService>();

var app = builder.Build();

// Pliki HMI z katalogu wwwroot. no-cache: po zmianie app.js wystarczy odświeżyć stronę.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache"
});
app.UseWebSockets();

app.Map("/ws", async (HttpContext context, HmiHub hub, IHostApplicationLifetime lifetime) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    // Przeglądarka pozwala dowolnej stronie WWW otworzyć WebSocket do localhost.
    // Bez tego sprawdzenia obca strona otwarta w drugiej karcie mogłaby wysłać "start".
    if (!IsSameOrigin(context.Request))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    string source = $"{context.Connection.RemoteIpAddress}:{context.Connection.RemotePort}";
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(
        context.RequestAborted, lifetime.ApplicationStopping);
    await hub.RunClientAsync(socket, source, cts.Token);
});

// Cały plik dziennika do pobrania (ekran pokazuje tylko ostatnie wpisy).
app.MapGet("/commands.jsonl", (CommandLog log) =>
    File.Exists(log.FilePath)
        ? Results.Stream(
            new FileStream(log.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite),
            "application/x-ndjson", "commands.jsonl")
        : Results.NotFound());

app.Run();

static bool IsSameOrigin(HttpRequest request)
{
    string? origin = request.Headers.Origin;
    if (string.IsNullOrEmpty(origin)) return true; // klient spoza przeglądarki (np. test)
    return Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
        && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
}

// Potrzebne, żeby projekt Tests mógł sięgnąć do typów bramy.
public partial class Program;
