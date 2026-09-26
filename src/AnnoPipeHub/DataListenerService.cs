using System.Net;
using System.Net.Sockets;

namespace AnnoPipeHub;

public sealed class DataListenerService(HubState hub, SettingsStore store, ILogger<DataListenerService> logger) : BackgroundService
{
    private readonly object _gate = new();
    private CancellationTokenSource _restart = new();
    private ListenerSettings _settings = new();
    private string _token = string.Empty;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _settings = store.Load();
        _token = store.LoadToken() ?? SettingsStore.NewToken();
        store.SaveToken(_token);
        store.Save(_settings);
        Console.WriteLine($"WebSocket: {(_settings.LanEnabled ? $"ws://{_settings.LanAddress}:{_settings.Port}/ws" : $"ws://127.0.0.1:{_settings.Port}/ws")}");
        Console.WriteLine($"LAN:       {(_settings.LanEnabled ? $"aktiv ({_settings.LanAddress})" : "deaktiviert")}");
        Console.WriteLine("Settings:  config\\hub-settings.json");
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunListenerAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Daten-Listener beendet.");
                hub.SetListener(hub.Listener with { State = "Error", Error = exception.Message });
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    public ListenerSettings Settings => _settings;
    public IReadOnlyList<NetworkInterfaceOption> Interfaces => NetworkSettings.PrivateAddresses();
    public string Token => _token;

    public Task<(bool Success, string? Error)> ApplyAsync(ListenerSettings requested)
    {
        if (!NetworkSettings.IsValidPort(requested.Port)) return Task.FromResult((false, (string?)"Der Port muss zwischen 1024 und 65535 liegen."));
        var address = requested.LanEnabled ? requested.LanAddress : "127.0.0.1";
        if (requested.LanEnabled && (address is null || !IPAddress.TryParse(address, out var ip) || !NetworkSettings.IsPrivate(ip) || ip.Equals(IPAddress.Loopback)))
            return Task.FromResult((false, (string?)"Für den LAN-Modus muss eine private IPv4-Adresse ausgewählt werden."));
        var candidate = requested with { LanAddress = address };
        var currentAddress = _settings.LanEnabled ? _settings.LanAddress : "127.0.0.1";
        if (candidate.Port != _settings.Port || !string.Equals(address, currentAddress, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var probe = new TcpListener(IPAddress.Parse(address!), candidate.Port);
                probe.Start(); probe.Stop();
            }
            catch (SocketException) { return Task.FromResult((false, (string?)$"Port {candidate.Port} ist bereits belegt oder nicht verfügbar.")); }
        }
        store.Save(candidate);
        lock (_gate) { _settings = candidate; _restart.Cancel(); _restart.Dispose(); _restart = new CancellationTokenSource(); }
        hub.SetListener(new ListenerStatus(candidate.LanEnabled, address!, candidate.Port, "Restarting", 0, null));
        return Task.FromResult((true, (string?)null));
    }

    public string RegenerateToken()
    {
        lock (_gate)
        {
            _token = SettingsStore.NewToken();
            store.SaveToken(_token);
            _restart.Cancel(); _restart.Dispose(); _restart = new CancellationTokenSource();
        }
        return _token;
    }

    private async Task RunListenerAsync(CancellationToken stoppingToken)
    {
        var settings = _settings; var token = _token; var address = settings.LanEnabled ? settings.LanAddress! : "127.0.0.1";
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _restart.Token);
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Parse(address), settings.Port));
        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        app.Map("/ws", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; await context.Response.WriteAsync("WebSocket erforderlich."); return; }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var endpoint = context.Connection.RemoteIpAddress is { } address
                ? $"{address}:{context.Connection.RemotePort}"
                : "unbekannt";
            await hub.RunClientAsync(socket, settings.LanEnabled, token, linked.Token, endpoint);
        });
        try
        {
            await app.StartAsync(linked.Token);
            hub.SetListener(new ListenerStatus(settings.LanEnabled, address, settings.Port, "Listening", 0, null));
            await app.WaitForShutdownAsync(linked.Token);
        }
        finally
        {
            try { await app.StopAsync(CancellationToken.None); } catch { }
            await app.DisposeAsync();
        }
    }
}
