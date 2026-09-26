using System.Reflection;
using AnnoPipeHub;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:8765");
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddSingleton<HubState>();
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<FileLogService>(sp => new FileLogService(sp.GetRequiredService<HubState>(), rawLoggingEnabled: string.Equals(Environment.GetEnvironmentVariable("ANNO117PIPEHUB_RAW_LOG"), "1", StringComparison.OrdinalIgnoreCase)));
builder.Services.AddHostedService(sp => sp.GetRequiredService<FileLogService>());
builder.Services.AddHostedService<PipeReaderService>();
builder.Services.AddSingleton<DataListenerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DataListenerService>());

var app = builder.Build();
app.UseDefaultFiles();
app.Lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine();
    Console.WriteLine("Anno 117 Pipe Hub gestartet");
    Console.WriteLine("Website:   http://127.0.0.1:8765/");
    Console.WriteLine("Status:    http://127.0.0.1:8765/api/status");
    Console.WriteLine(@"Pipe:      \\.\pipe\anno117 (nur Lesen)");
    Console.WriteLine("Hinweis:   Diese Konsole offen lassen, solange der Hub laufen soll.");
    Console.WriteLine();
});

app.MapGet("/", () => Results.Content(ReadDashboardFile("index.html"), "text/html; charset=utf-8"));
app.MapGet("/app.css", () => Results.Content(ReadDashboardFile("app.css"), "text/css; charset=utf-8"));
app.MapGet("/app.js", () => Results.Content(ReadDashboardFile("app.js"), "text/javascript; charset=utf-8"));

app.MapGet("/api/status", (HubState hub) => Results.Json(new { schemaVersion = 1, type = "hub.status", receivedAtUtc = DateTimeOffset.UtcNow, hub = hub.Status, listener = hub.Listener, clients = hub.Clients, fileLog = hub.FileLog }));
app.MapGet("/api/debug", (HubState hub) => Results.Json(new { entries = hub.DebugLog }));
app.MapPost("/api/debug/clear", (HubState hub) => { hub.ClearDebugLog(); return Results.Ok(new { ok = true }); });
app.MapPost("/api/file-log", async (FileLogRequest request, FileLogService fileLog) =>
{
    var status = request.Enabled ? await fileLog.EnableAsync() : await fileLog.DisableLoggingAsync();
    return Results.Json(new { ok = status.Available || !request.Enabled, fileLog = status });
});
app.MapGet("/api/settings", (DataListenerService listener) => Results.Json(new { settings = listener.Settings, token = listener.Settings.LanEnabled ? listener.Token : null, interfaces = listener.Interfaces }));
app.MapPost("/api/settings", async (ListenerSettings settings, DataListenerService listener) =>
{
    var result = await listener.ApplyAsync(settings);
    return result.Success ? Results.Ok(new { ok = true, listener = listener.Settings, token = listener.Settings.LanEnabled ? listener.Token : null }) : Results.BadRequest(new { ok = false, error = result.Error });
});
app.MapPost("/api/token/regenerate", (DataListenerService listener) => Results.Ok(new { token = listener.RegenerateToken() }));

app.MapFallback(() => Results.Content(ReadDashboardFile("index.html"), "text/html; charset=utf-8"));
await app.RunAsync();

static string ReadDashboardFile(string fileName)
{
    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"AnnoPipeHub.WebContent.{fileName}")
        ?? throw new InvalidOperationException($"Dashboard-Datei fehlt: {fileName}");
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

public sealed record FileLogRequest(bool Enabled);