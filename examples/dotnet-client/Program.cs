using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var endpoint = args.Length > 0 ? args[0] : "ws://127.0.0.1:8766/ws";
var token = args.Length > 1 ? args[1] : null;
using var stop = new CancellationTokenSource(); Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
while (!stop.IsCancellationRequested)
{
    using var socket = new ClientWebSocket();
    try
    {
        await socket.ConnectAsync(new Uri(endpoint), stop.Token);
        Console.WriteLine($"Verbunden: {endpoint}");
        if (!string.IsNullOrWhiteSpace(token)) await SendJsonAsync(socket, new { type = "auth", token }, stop.Token);
        while (socket.State == WebSocketState.Open && !stop.IsCancellationRequested)
        {
            var message = await ReceiveTextAsync(socket, stop.Token);
            if (message is null) break;
            try
            {
                using var json = JsonDocument.Parse(message);
                var type = json.RootElement.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : "unbekannt";
                Console.WriteLine($"[{DateTimeOffset.Now:T}] {type}: {json.RootElement}");
            }
            catch (JsonException) { Console.WriteLine("Ungültige JSON-Nachricht verworfen."); }
        }
    }
    catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
    catch (Exception exception) { Console.WriteLine($"Verbindung fehlgeschlagen: {exception.Message}"); }
    if (!stop.IsCancellationRequested) { Console.WriteLine("Reconnect in 2 Sekunden..."); await Task.Delay(2000, stop.Token); }
}

static async Task SendJsonAsync(ClientWebSocket socket, object value, CancellationToken token)
{
    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
    await socket.SendAsync(bytes, WebSocketMessageType.Text, true, token);
}
static async Task<string?> ReceiveTextAsync(ClientWebSocket socket, CancellationToken token)
{
    using var buffer = new MemoryStream(); var chunk = new byte[8192];
    WebSocketReceiveResult result;
    do { result = await socket.ReceiveAsync(chunk, token); if (result.MessageType == WebSocketMessageType.Close) return null; buffer.Write(chunk, 0, result.Count); } while (!result.EndOfMessage);
    return Encoding.UTF8.GetString(buffer.ToArray());
}
