using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace AnnoPipeHub;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipeConnectionState { Waiting, Connected, Disconnected, Timeout, NotFound, Busy }

public sealed record HubStatusSnapshot(string HubStatus, PipeConnectionState PipeStatus, int ConnectedClients,
    DateTimeOffset? LastMessageAtUtc, int? ProtocolVersion, string? Error);
public sealed record PipeErrorDetails(string Category, string Severity, string ApiOperation, int WindowsCode, string WindowsName,
    string EventKey, IReadOnlyDictionary<string, object?> Parameters);

public sealed record DebugLogEntry(DateTimeOffset AtUtc, string Kind, string Message,
    int? MessageSize = null, int? ProtocolVersion = null, string? MessageType = null,
    object? Details = null, string? RawHex = null, PipeErrorDetails? Error = null,
    string? EventKey = null, IReadOnlyDictionary<string, object?>? Parameters = null);

public sealed record ConnectedClient(Guid Id, DateTimeOffset ConnectedAtUtc, string Endpoint);
public sealed record FileLogStatus(bool Enabled, bool Available, string? Path, string? Error);
public interface IFileLogSink { void Enqueue(DebugLogEntry entry); }

public sealed class HubState
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<Guid, ClientConnection> _clients = new();
    private readonly Dictionary<string, DecodedPipeMessage> _snapshots = new(StringComparer.Ordinal);
    private readonly Queue<DebugLogEntry> _debugLog = new();
    private const int MaxDebugEntries = 200;
    private IFileLogSink? _fileLog;
    private FileLogStatus _fileLogStatus = new(false, false, null, null);
    private string? _lastDebugSignature;
    private DateTimeOffset _lastDebugAtUtc;
    private HubStatusSnapshot _status = new("Bereit", PipeConnectionState.Waiting, 0, null, null, null);
    private ListenerStatus _listener = ListenerStatus.Default;

    public HubStatusSnapshot Status { get { lock (_gate) return _status with { ConnectedClients = _clients.Count }; } }
    public ListenerStatus Listener { get { lock (_gate) return _listener with { ConnectedClients = _clients.Count }; } }
    public IReadOnlyList<DecodedPipeMessage> Snapshots { get { lock (_gate) return _snapshots.Values.ToArray(); } }
    public IReadOnlyList<DebugLogEntry> DebugLog { get { lock (_gate) return _debugLog.ToArray(); } }
    public IReadOnlyList<ConnectedClient> Clients => _clients.Values.Select(client => client.Info).OrderBy(client => client.ConnectedAtUtc).ToArray();
    public FileLogStatus FileLog { get { lock (_gate) return _fileLogStatus; } }

    public void AttachFileLog(IFileLogSink fileLog) { lock (_gate) _fileLog = fileLog; }
    public void SetFileLogStatus(FileLogStatus status) { lock (_gate) _fileLogStatus = status; }
    public void ClearDebugLog() { lock (_gate) _debugLog.Clear(); }

    public void SetListener(ListenerStatus listener) { lock (_gate) _listener = listener with { ConnectedClients = _clients.Count }; BroadcastStatus(); }
    public void SetPipeState(PipeConnectionState state, string? error = null, string? eventKey = null, object? parameters = null) { lock (_gate) _status = _status with { PipeStatus = state, Error = error }; AddDebug("pipe", eventKey ?? $"pipe.{state.ToString().ToLowerInvariant()}", parameters); BroadcastStatus(); }
    public void SetProtocolVersion(int version) { var eventKey = version == PipeDecoder.ProtocolVersion ? "protocol.version" : "protocol.unsupported"; lock (_gate) _status = _status with { ProtocolVersion = version, Error = version == PipeDecoder.ProtocolVersion ? null : eventKey }; AddDebug("protocol", eventKey, new { version }, protocolVersion: version); BroadcastStatus(); }

    public void AddDebug(string kind, string eventKey, object? parameters = null, int? messageSize = null,
        int? protocolVersion = null, string? messageType = null, object? structuredDetails = null, byte[]? rawFrame = null,
        PipeErrorDetails? error = null)
    {
        var rawHex = rawFrame is null ? null : Convert.ToHexString(rawFrame.AsSpan(0, Math.Min(rawFrame.Length, 256)));
        var now = DateTimeOffset.UtcNow;
        var signature = $"{kind}|{eventKey}|{parameters}|{messageSize}|{protocolVersion}|{messageType}|{structuredDetails}|{error}";
        lock (_gate)
        {
            if (kind is "pipe" or "error" && signature == _lastDebugSignature && now - _lastDebugAtUtc < TimeSpan.FromSeconds(5)) return;
            _lastDebugSignature = signature;
            _lastDebugAtUtc = now;
            _debugLog.Enqueue(new DebugLogEntry(now, kind, eventKey, messageSize,
                protocolVersion, messageType, structuredDetails, rawHex, error, eventKey, ToParameters(parameters)));
            while (_debugLog.Count > MaxDebugEntries) _debugLog.Dequeue();
            _fileLog?.Enqueue(_debugLog.Last());
        }
    }

    private static IReadOnlyDictionary<string, object?>? ToParameters(object? parameters) => parameters switch
    {
        null => null,
        IReadOnlyDictionary<string, object?> dictionary => dictionary,
        _ => parameters.GetType().GetProperties().ToDictionary(property => property.Name, property => property.GetValue(parameters))
    };

    public void Accept(DecodedPipeMessage message, int? messageSize = null, byte[]? rawFrame = null)
    {
        lock (_gate)
        {
            _status = _status with { LastMessageAtUtc = DateTimeOffset.UtcNow, Error = null };
            if (message.Type is PipeMessageType.SessionStart or PipeMessageType.SessionEnd) _snapshots.Clear();
            else if (message.Statistics is { } stats) _snapshots[$"{stats.SessionGuid}:{stats.SessionId}:{stats.IslandId}:{stats.AreaIndex}"] = message;
        }
        AddDebug("message", "message.received", new { type = message.Type.ToString() }, messageSize: messageSize, protocolVersion: message.Version,
            messageType: message.Type.ToString(), structuredDetails: DebugDetails(message), rawFrame: rawFrame);
        Broadcast(JsonSerializer.Serialize(ToWireMessage(message), JsonDefaults.Options));
        BroadcastStatus();
    }

    private static object? DebugDetails(DecodedPipeMessage message) => message.Type switch
    {
        PipeMessageType.Version => new { version = message.Version },
        PipeMessageType.SessionStart => new { sessionName = message.SessionStart?.SessionName },
        PipeMessageType.AreaProductionStatistics when message.Statistics is { } statistics => new
        {
            sessionId = statistics.SessionId, islandId = statistics.IslandId, areaIndex = statistics.AreaIndex,
            sessionGuid = statistics.SessionGuid, areaName = statistics.AreaName, rawTimestamp = statistics.RawTimestamp,
            entries = statistics.Entries.Count
        },
        _ => null
    };

    public async Task RunClientAsync(WebSocket socket, bool requiresAuthentication, string token, CancellationToken cancellationToken, string? endpoint = null)
    {
        var client = new ClientConnection(socket, endpoint ?? "unbekannt");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            if (requiresAuthentication && !await AuthenticateAsync(socket, lifetime.Token)) return;
            _clients[client.Id] = client;
            client.Enqueue(JsonSerializer.Serialize(ToWireStatusMessage(), JsonDefaults.Options));
            client.Enqueue(JsonSerializer.Serialize(ToSnapshotMessage(), JsonDefaults.Options));
            BroadcastStatus();
            var sendTask = client.SendLoopAsync(lifetime.Token);
            var receiveTask = MonitorDisconnectAsync(socket, lifetime.Token);
            await Task.WhenAny(sendTask, receiveTask);
            lifetime.Cancel();
            try { await Task.WhenAll(sendTask, receiveTask); }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
        }
        finally
        {
            _clients.TryRemove(client.Id, out _); client.Complete();
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Verbindung beendet", CancellationToken.None); }
                catch (WebSocketException) { }
            }
            BroadcastStatus();
        }

        async Task<bool> AuthenticateAsync(WebSocket webSocket, CancellationToken tokenCancellation)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(tokenCancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var buffer = new byte[4096]; var result = await webSocket.ReceiveAsync(buffer, timeout.Token);
                if (result.MessageType != WebSocketMessageType.Text || !result.EndOfMessage) return false;
                using var document = JsonDocument.Parse(buffer.AsMemory(0, result.Count)); var root = document.RootElement;
                return root.TryGetProperty("type", out var type) && type.GetString() == "auth" && root.TryGetProperty("token", out var supplied) && CryptographicTokenComparer.Equals(supplied.GetString(), token);
            }
            catch (OperationCanceledException) { return false; }
            catch (JsonException) { return false; }
        }

        static async Task MonitorDisconnectAsync(WebSocket webSocket, CancellationToken tokenCancellation)
        {
            var buffer = new byte[1024];
            while (!tokenCancellation.IsCancellationRequested && webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(buffer, tokenCancellation);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
    }

    private object ToWireStatusMessage() => new { schemaVersion = 1, type = "hub.status", receivedAtUtc = DateTimeOffset.UtcNow, hub = new { status = Status, listener = Listener } };
    private object ToSnapshotMessage() => new { schemaVersion = 1, type = "state.snapshot", receivedAtUtc = DateTimeOffset.UtcNow, snapshots = Snapshots.Select(ToWireMessage).ToArray() };
    private void BroadcastStatus() => Broadcast(JsonSerializer.Serialize(ToWireStatusMessage(), JsonDefaults.Options));
    private void Broadcast(string json) { foreach (var client in _clients.Values) client.Enqueue(json); }

    private static object ToWireMessage(DecodedPipeMessage message) => message.Type switch
    {
        PipeMessageType.SessionStart => new { schemaVersion = 1, type = "session.start", receivedAtUtc = DateTimeOffset.UtcNow, session = message.SessionStart },
        PipeMessageType.SessionEnd => new { schemaVersion = 1, type = "session.end", receivedAtUtc = DateTimeOffset.UtcNow },
        PipeMessageType.AreaProductionStatistics => new { schemaVersion = 1, type = "area.production.statistics", receivedAtUtc = DateTimeOffset.UtcNow, statistics = message.Statistics },
        _ => new { schemaVersion = 1, type = "hub.status", receivedAtUtc = DateTimeOffset.UtcNow }
    };

    private sealed class ClientConnection(WebSocket socket, string endpoint)
    {
        private readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        public Guid Id { get; } = Guid.NewGuid();
        private DateTimeOffset ConnectedAtUtc { get; } = DateTimeOffset.UtcNow;
        public ConnectedClient Info => new(Id, ConnectedAtUtc, endpoint);
        public void Enqueue(string message) => _queue.Writer.TryWrite(message);
        public void Complete() => _queue.Writer.TryComplete();
        public async Task SendLoopAsync(CancellationToken cancellationToken)
        {
            await foreach (var message in _queue.Reader.ReadAllAsync(cancellationToken))
            {
                if (socket.State != WebSocketState.Open) break;
                await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, cancellationToken);
            }
        }
    }
}

public static class CryptographicTokenComparer
{
    public static bool Equals(string? left, string? right)
    {
        if (left is null || right is null) return false;
        var leftBytes = Encoding.UTF8.GetBytes(left); var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = false };
}
