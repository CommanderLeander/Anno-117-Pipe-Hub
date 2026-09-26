using System.Buffers.Binary;
using System.ComponentModel;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Http;
using AnnoPipeHub;

var tests = new (string Name, Action Test)[]
{
    ("Preamble/Version", TestVersion),
    ("SessionStart", TestSessionStart),
    ("SessionEnd", TestSessionEnd),
    ("AreaProductionStatistics", TestStatistics),
    ("Ungültige Nachrichtenlänge", TestInvalidLength),
    ("Abgeschnittene Daten", TestTruncated),
    ("JSON-Schema-Fixtures", TestSchemaFixtures),
    ("Listener-Regeln", TestListenerRules),
    ("Snapshot-Zustand", TestSnapshotState),
    ("Debug-Log begrenzt und dekodiert", TestDebugLog),
    ("Pipe-Fehlercodes", TestPipeConnectionErrors),
    ("Strukturierter Pipe-Fehlerlog", TestStructuredPipeErrorLog),
    ("Datei-Log-Pfad", TestFileLogPath),
    ("Datei-Log Opt-in", TestFileLogOptIn),
    ("Datei-Log Fehler und Reaktivierung", TestFileLogRecovery),
    ("Debug-Log leeren ohne Datei-Log", TestClearDebugLog),
    ("Multi-Client-Fanout-ohne-Blockierung", TestFanout),
    ("Origin-Schutz", TestOriginPolicy),
    ("Sprachneutrale Statuswerte", TestStatusValues)
};
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception exception) { Console.Error.WriteLine($"FAIL {name}: {exception.Message}"); Environment.ExitCode = 1; }
}

static void TestVersion()
{
    var payload = Payload(PipeMessageType.Version, b => WriteInt32(b, 2));
    var message = PipeDecoder.DecodeFrame(payload.ToArray(), true);
    Assert(message.Type == PipeMessageType.Version && message.Version == 2, "Version wurde nicht dekodiert.");
}

static void TestSessionStart()
{
    var message = PipeDecoder.DecodeFrame(Payload(PipeMessageType.SessionStart, b => WriteString(b, "Testsession")).ToArray());
    Assert(message.SessionStart?.SessionName == "Testsession", "SessionStart wurde nicht dekodiert.");
}

static void TestSessionEnd()
{
    var message = PipeDecoder.DecodeFrame(Payload(PipeMessageType.SessionEnd).ToArray());
    Assert(message.Type == PipeMessageType.SessionEnd, "SessionEnd wurde nicht dekodiert.");
}

static void TestStatistics()
{
    var payload = Payload(PipeMessageType.AreaProductionStatistics, b =>
    {
        b.AddRange([7, 8, 9]); WriteInt32(b, 1234); WriteString(b, "North Sea"); WriteInt64(b, 987654321); WriteInt32(b, 1);
        WriteInt32(b, 42); foreach (var value in new[] { 10f, 2f, 8f, 12f, 1f }) WriteSingle(b, value);
        WriteInt32(b, 3); WriteInt32(b, 4); WriteSingle(b, 100.5f); WriteInt32(b, 90); WriteSingle(b, 5.5f); WriteSingle(b, 4.5f);
        WriteInt32(b, 1); WriteInt32(b, 500); WriteInt32(b, 12); WriteInt32(b, 1); WriteInt32(b, 600); WriteInt32(b, 3);
    });
    var stats = PipeDecoder.DecodeFrame(payload.ToArray()).Statistics!;
    var entry = stats.Entries.Single();
    Assert(stats.AreaName == "North Sea" && stats.Entries.Count == 1 && entry.ProductGuid == 42, "Statistikdaten fehlen.");
    Assert(entry.WorkforceGuidToAmount[500] == 12 && entry.BuildingGuidToAmount[600] == 3, "GUID-Mengen fehlen.");
}

static void TestInvalidLength()
{
    var frame = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(frame, PipeDecoder.MaxMessageSize + 1);
    AssertThrows<ProtocolException>(() => PipeDecoder.ValidateMessageLength(BinaryPrimitives.ReadInt32LittleEndian(frame)));
}

static void TestTruncated()
{
    AssertThrows<ProtocolException>(() => PipeDecoder.DecodeFrame([ (byte)PipeMessageType.Version ]));
}

static void TestSchemaFixtures()
{
    var schema = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "schema", "anno117-event.schema.json"));
    var fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "events.json"));
    using var schemaJson = System.Text.Json.JsonDocument.Parse(schema);
    using var events = System.Text.Json.JsonDocument.Parse(fixture);
    Assert(schemaJson.RootElement.TryGetProperty("oneOf", out _), "Schema oneOf fehlt.");
    var types = events.RootElement.EnumerateArray().Select(x => x.GetProperty("type").GetString()).ToHashSet();
    Assert(types.SetEquals(["hub.status", "state.snapshot", "session.start", "session.end", "area.production.statistics"]), "Nicht alle Ereignis-Fixtures vorhanden.");
    foreach (var item in events.RootElement.EnumerateArray()) { Assert(item.GetProperty("schemaVersion").GetInt32() == 1, "Ungültige Schema-Version."); Assert(item.GetProperty("receivedAtUtc").ValueKind == System.Text.Json.JsonValueKind.String, "Zeitstempel fehlt."); }
}

static void TestListenerRules()
{
    Assert(NetworkSettings.IsValidPort(8766) && !NetworkSettings.IsValidPort(80) && !NetworkSettings.IsValidPort(70000), "Portregeln fehlerhaft.");
    Assert(NetworkSettings.IsPrivate(System.Net.IPAddress.Parse("192.168.1.25")), "Private IPv4 nicht erkannt.");
    Assert(NetworkSettings.IsPrivate(System.Net.IPAddress.Parse("10.0.0.5")) && NetworkSettings.IsPrivate(System.Net.IPAddress.Parse("172.16.0.5")), "RFC1918-Adresse nicht erkannt.");
    Assert(!NetworkSettings.IsPrivate(System.Net.IPAddress.Parse("127.0.0.1")) && !NetworkSettings.IsPrivate(System.Net.IPAddress.Parse("127.0.0.2")), "Loopback-Adresse fälschlich als LAN-Adresse akzeptiert.");
    Assert(!NetworkSettings.IsPrivate(System.Net.IPAddress.Parse("8.8.8.8")), "Öffentliche IPv4 fälschlich akzeptiert.");
}

static void TestSnapshotState()
{
    var hub = new HubState();
    var message = PipeDecoder.DecodeFrame(Payload(PipeMessageType.AreaProductionStatistics, b => { b.AddRange([1, 2, 3]); WriteInt32(b, 9); WriteString(b, "Area"); WriteInt64(b, 1); WriteInt32(b, 0); }).ToArray());
    hub.Accept(message);
    Assert(hub.Snapshots.Count == 1, "Statistik-Snapshot wurde nicht gehalten.");
}

static void TestDebugLog()
{
    var hub = new HubState();
    for (var index = 0; index < 205; index++) hub.AddDebug("message", $"Eintrag {index}");
    var message = PipeDecoder.DecodeFrame(Payload(PipeMessageType.AreaProductionStatistics, b =>
    {
        b.AddRange([1, 2, 3]); WriteInt32(b, 1234); WriteString(b, "Area"); WriteInt64(b, 1); WriteInt32(b, 1);
        WriteInt32(b, 42); foreach (var value in new[] { 10f, 2f, 8f, 12f, 1f }) WriteSingle(b, value);
        WriteInt32(b, 3); WriteInt32(b, 4); WriteSingle(b, 100.5f); WriteInt32(b, 90); WriteSingle(b, 5.5f); WriteSingle(b, 4.5f);
        WriteInt32(b, 1); WriteInt32(b, 500); WriteInt32(b, 12); WriteInt32(b, 1); WriteInt32(b, 600); WriteInt32(b, 3);
    }).ToArray());
    hub.Accept(message, 1, [3]);
    Assert(hub.DebugLog.Count == 200, "Debug-Log ist nicht auf 200 Einträge begrenzt.");
    Assert(hub.DebugLog[0].Message == "Eintrag 6", "Älteste Debug-Einträge wurden nicht verworfen.");
    Assert(hub.DebugLog[^1].MessageType == "AreaProductionStatistics" && hub.DebugLog[^1].Details is not null, "Dekodierte Debug-Details fehlen.");
    var details = System.Text.Json.JsonSerializer.Serialize(hub.DebugLog[^1].Details);
    Assert(details.Contains("\"productGuid\":42", StringComparison.Ordinal) && details.Contains("\"productGeneration\":10", StringComparison.Ordinal) && details.Contains("\"productConsumption\":2", StringComparison.Ordinal) && details.Contains("\"productDelta\":8", StringComparison.Ordinal), "Produktwerte fehlen in den Debugdetails.");
    Assert(details.Contains("workforceGuidToAmount", StringComparison.Ordinal) && details.Contains("\"500\":12", StringComparison.Ordinal) && details.Contains("buildingGuidToAmount", StringComparison.Ordinal) && details.Contains("\"600\":3", StringComparison.Ordinal), "GUID-Mengen fehlen in den Debugdetails.");
}

static void TestPipeConnectionErrors()
{
    var notFound = PipeConnectionErrors.Classify(new Win32Exception(2), "NamedPipeClientStream.ConnectAsync");
    var busy = PipeConnectionErrors.Classify(new Win32Exception(231), "NamedPipeClientStream.ConnectAsync");
    var timeout = PipeConnectionErrors.Classify(new TimeoutException(), "NamedPipeClientStream.ConnectAsync");
    var unknown = PipeConnectionErrors.Classify(new Win32Exception(123), "Stream.ReadAsync");
    Assert(notFound.Kind == PipeFailureKind.NotFound && notFound.Category == "pipe.notFound", "ERROR_FILE_NOT_FOUND wurde falsch zugeordnet.");
    Assert(busy.Kind == PipeFailureKind.Busy && busy.Category == "pipe.busy", "ERROR_PIPE_BUSY wurde falsch zugeordnet.");
    Assert(timeout.Kind == PipeFailureKind.Timeout && timeout.ErrorCode == 1460 && timeout.WindowsName == "ERROR_TIMEOUT" && timeout.Category == "pipe.timeout", "Timeout wurde falsch zugeordnet.");
    Assert(timeout.Category != "pipe.busy" && timeout.ApiOperation == "NamedPipeClientStream.ConnectAsync", "Timeout wurde fälschlich als belegte Pipe angezeigt.");
    Assert(notFound.ApiOperation == "NamedPipeClientStream.ConnectAsync" && busy.ErrorCode == 231, "API-Operation oder Fehlercode fehlen.");
    Assert(unknown.Kind == PipeFailureKind.Other && unknown.Category == "pipe.unknown" && unknown.ApiOperation == "Stream.ReadAsync", "Unbekannter Fehler wurde pauschal zugeordnet.");
}

static void TestFileLogPath()
{
    var root = Path.Combine(Environment.CurrentDirectory, ".local", "log-test");
    var path = FileLogService.GetLogPath(root, new DateOnly(2026, 9, 25));
    Assert(path == Path.Combine(root, "Anno117PipeHub-2026-09-25.log"), "Datei-Log-Pfad ist nicht tagesbezogen.");
    Assert(path.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase), "Test-Log-Pfad verlässt den Workspace-Testordner.");
}

static void TestStructuredPipeErrorLog()
{
    var failure = PipeConnectionErrors.Classify(new TimeoutException(), "NamedPipeClientStream.ConnectAsync");
    var error = new PipeErrorDetails(failure.Category, failure.Severity, failure.ApiOperation,
        failure.ErrorCode, failure.WindowsName, failure.EventKey, failure.Parameters);
    var entry = new DebugLogEntry(new DateTimeOffset(2026, 9, 25, 20, 26, 10, TimeSpan.FromHours(2)), "error", failure.EventKey, Error: error);
    var line = FileLogService.FormatLogLine(entry);
    var json = System.Text.Json.JsonSerializer.Serialize(entry, JsonDefaults.Options);
    Assert(line.Contains("[WARN] [PIPE.TIMEOUT]", StringComparison.Ordinal) && line.Contains("Win32 1460: ERROR_TIMEOUT", StringComparison.Ordinal), "Timeout fehlt korrekt im Datei-Log.");
    Assert(line.Contains("API NamedPipeClientStream.ConnectAsync", StringComparison.Ordinal) && !line.Contains("decoded=", StringComparison.Ordinal), "API-Operation oder Fehlerkategorie im Datei-Log fehlt.");
    Assert(json.Contains("\"windowsCode\":1460", StringComparison.Ordinal) && json.Contains("\"apiOperation\":\"NamedPipeClientStream.ConnectAsync\"", StringComparison.Ordinal), "Strukturierte Fehlerdetails fehlen im GUI-JSON.");
}

static void TestFileLogOptIn()
{
    var root = Path.Combine(Environment.CurrentDirectory, ".local", "opt-in-log-test");
    if (Directory.Exists(root)) Directory.Delete(root, true);
    var hub = new HubState();
    var service = new FileLogService(hub, root);
    service.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
    hub.AddDebug("message", "message.before");
    Assert(!Directory.Exists(root) || !Directory.EnumerateFiles(root, "*.log").Any(), "Ohne Opt-in wurde eine Logdatei angelegt.");
    var enabled = service.EnableAsync().GetAwaiter().GetResult();
    Assert(enabled.Enabled && enabled.Available && enabled.Path is not null && File.Exists(enabled.Path), "Opt-in öffnet keine Datei neben dem konfigurierten Pfad.");
    hub.AddDebug("message", "message.after");
    Thread.Sleep(100);
    service.DisableLoggingAsync().GetAwaiter().GetResult();
    var content = File.ReadAllText(enabled.Path!);
    Assert(content.Contains("message.after", StringComparison.Ordinal) && !content.Contains("message.before", StringComparison.Ordinal), $"Beim Einschalten wurden alte RAM-Einträge nachträglich gespeichert: {content}");
    service.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    Directory.Delete(root, true);
}

static void TestFileLogRecovery()
{
    var root = Path.Combine(Environment.CurrentDirectory, ".local", "file-log-retry-test");
    if (Directory.Exists(root)) Directory.Delete(root, true);
    Directory.CreateDirectory(Path.GetDirectoryName(root)!);
    File.WriteAllText(root, "blocker");
    var hub = new HubState();
    var service = new FileLogService(hub, root);
    service.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
    var failed = service.EnableAsync().GetAwaiter().GetResult();
    Assert(!failed.Available && failed.Error == "filelog.unavailable", "Schreibfehler wird nicht sichtbar gemeldet.");
    File.Delete(root);
    Directory.CreateDirectory(root);
    var recovered = service.EnableAsync().GetAwaiter().GetResult();
    Assert(recovered.Enabled && recovered.Available, "Datei-Log lässt sich nach Schreibfehler nicht reaktivieren.");
    service.DisableLoggingAsync().GetAwaiter().GetResult();
    service.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    Directory.Delete(root, true);
}

static void TestOriginPolicy()
{
    var allowedHost = new DefaultHttpContext(); allowedHost.Request.Host = new HostString("127.0.0.1", 8765);
    var forbiddenHost = new DefaultHttpContext(); forbiddenHost.Request.Host = new HostString("127.0.0.1", 8766);
    Assert(RequestSecurity.IsAllowedDashboardGet(allowedHost.Request), "Legitimer Dashboard-Host wurde abgelehnt.");
    Assert(!RequestSecurity.IsAllowedDashboardGet(forbiddenHost.Request), "Fremder Host wurde für Dashboard-GET akzeptiert.");
    Assert(RequestSecurity.IsAllowedOrigin(null), "Native Client ohne Origin wurde abgelehnt.");
    Assert(RequestSecurity.IsAllowedOrigin(RequestSecurity.DashboardOrigin), "Dashboard-Origin wurde abgelehnt.");
    Assert(!RequestSecurity.IsAllowedOrigin("https://evil.example"), "Fremde Browser-Origin wurde akzeptiert.");
    Assert(!RequestSecurity.IsAllowedOrigin("http://127.0.0.1:8766"), "WebSocket-Origin des Datenports wurde unerwartet akzeptiert.");
}

static void TestStatusValues()
{
    var status = new HubState().Status;
    Assert(status.HubStatus == "Ready", "hubStatus ist nicht sprachneutral.");
    Assert(Enum.GetValues<PipeConnectionState>().ToHashSet().SetEquals([PipeConnectionState.Waiting, PipeConnectionState.Connected, PipeConnectionState.Disconnected, PipeConnectionState.Timeout, PipeConnectionState.NotFound, PipeConnectionState.Busy]), "Nicht alle Pipe-Statuswerte sind modelliert.");
}

static void TestClearDebugLog()
{
    var root = Path.Combine(Environment.CurrentDirectory, ".local", "clear-log-test");
    if (Directory.Exists(root)) Directory.Delete(root, true);
    var hub = new HubState();
    var service = new FileLogService(hub, root);
    service.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
    var enabled = service.EnableAsync().GetAwaiter().GetResult();
    hub.AddDebug("message", "message.persisted");
    Thread.Sleep(100);
    service.DisableLoggingAsync().GetAwaiter().GetResult();
    var beforeClear = File.ReadAllText(enabled.Path!);
    hub.ClearDebugLog();
    Assert(hub.DebugLog.Count == 0, "Der In-Memory-Debugpuffer wurde nicht geleert.");
    Assert(File.ReadAllText(enabled.Path!) == beforeClear && beforeClear.Contains("message.persisted", StringComparison.Ordinal), "Das Datei-Log wurde beim Leeren verändert.");
    service.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    Directory.Delete(root, true);
}

static void TestFanout()
{
    var hub = new HubState(); var fast = new TestSocket(); var slow = new TestSocket(TimeSpan.FromMilliseconds(100));
    using var cancellation = new CancellationTokenSource();
    var fastTask = hub.RunClientAsync(fast, false, "", cancellation.Token);
    var slowTask = hub.RunClientAsync(slow, false, "", cancellation.Token);
    Task.Delay(100).Wait();
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    hub.Accept(PipeDecoder.DecodeFrame(Payload(PipeMessageType.SessionStart, b => WriteString(b, "Fanout")).ToArray()));
    stopwatch.Stop();
    Task.Delay(500).Wait(); cancellation.Cancel();
    try { Task.WhenAll(fastTask, slowTask).Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException exception) when (exception.InnerExceptions.All(e => e is TaskCanceledException or OperationCanceledException)) { }
    Assert(stopwatch.Elapsed < TimeSpan.FromMilliseconds(100), "Ein langsamer Client blockiert den Hub.");
    Assert(fast.Messages.Any(m => m.Contains("session.start")) && slow.Messages.Any(m => m.Contains("session.start")), "Live-Ereignis wurde nicht an alle Clients verteilt.");
    Assert(hub.Listener.ConnectedClients == 0, "Beendete Clients wurden nicht aus dem Counter entfernt.");
}

static List<byte> Payload(PipeMessageType type, Action<List<byte>>? write = null) { var bytes = new List<byte> { (byte)type }; write?.Invoke(bytes); return bytes; }
static void WriteInt32(List<byte> bytes, int value) { var buffer = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(buffer, value); bytes.AddRange(buffer); }
static void WriteInt64(List<byte> bytes, long value) { var buffer = new byte[8]; BinaryPrimitives.WriteInt64LittleEndian(buffer, value); bytes.AddRange(buffer); }
static void WriteSingle(List<byte> bytes, float value) => WriteInt32(bytes, BitConverter.SingleToInt32Bits(value));
static void WriteString(List<byte> bytes, string value) { var encoded = System.Text.Encoding.UTF8.GetBytes(value); bytes.Add((byte)encoded.Length); bytes.AddRange(encoded); }
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void AssertThrows<T>(Action action) where T : Exception { try { action(); throw new InvalidOperationException("Erwartete Ausnahme wurde nicht ausgelöst."); } catch (T) { } }

sealed class TestSocket(TimeSpan? sendDelay = null) : WebSocket
{
    private WebSocketState _state = WebSocketState.Open;
    public List<string> Messages { get; } = [];
    public override WebSocketCloseStatus? CloseStatus => null;
    public override string? CloseStatusDescription => null;
    public override WebSocketState State => _state;
    public override string? SubProtocol => null;
    public override void Abort() => _state = WebSocketState.Aborted;
    public override void Dispose() => _state = WebSocketState.Closed;
    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) { _state = WebSocketState.Closed; return Task.CompletedTask; }
    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true); }
    public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        if (sendDelay is { } delay) await Task.Delay(delay, cancellationToken);
        Messages.Add(Encoding.UTF8.GetString(buffer));
    }
}
