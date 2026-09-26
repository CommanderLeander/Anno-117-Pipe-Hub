using System.Buffers.Binary;
using System.IO.Pipes;

namespace AnnoPipeHub;

public sealed class PipeReaderService(HubState hub, ILogger<PipeReaderService> logger) : BackgroundService
{
    private const string PipeName = "anno117";
    private string? _lastFailure;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            var apiOperation = "NamedPipeClientStream.ConnectAsync";
            try
            {
                hub.AddDebug("pipe", "pipe.connecting", new { apiOperation = "NamedPipeClientStream.ConnectAsync" });
                hub.SetPipeState(PipeConnectionState.Waiting, eventKey: "pipe.waiting");
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.In, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(TimeSpan.FromSeconds(2), stoppingToken);
                delay = TimeSpan.FromSeconds(1);
                hub.SetPipeState(PipeConnectionState.Connected, eventKey: "pipe.connected");
                apiOperation = "Stream.ReadAsync";
                await ReadConnectionAsync(pipe, stoppingToken);
                hub.SetPipeState(PipeConnectionState.Disconnected, eventKey: "pipe.disconnected");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (TimeoutException exception) { await HandleConnectionFailureAsync(exception, apiOperation, delay, stoppingToken); delay = IncreaseDelay(delay); }
            catch (IOException exception)
            {
                logger.LogDebug(exception, "Pipe-Verbindung unterbrochen.");
                await HandleConnectionFailureAsync(exception, apiOperation, delay, stoppingToken);
                delay = IncreaseDelay(delay);
            }
            catch (UnauthorizedAccessException exception)
            {
                await HandleConnectionFailureAsync(exception, apiOperation, delay, stoppingToken);
                delay = IncreaseDelay(delay);
            }
            catch (ProtocolException exception)
            {
                logger.LogWarning(exception, "Ungültige Pipe-Nachricht.");
                hub.SetPipeState(PipeConnectionState.Disconnected, "decoder.invalid", "decoder.invalid");
                hub.AddDebug("decoder", "decoder.invalid");
                hub.AddDebug("reconnect", "reconnect.scheduled", new { seconds = delay.TotalSeconds });
                await WaitBeforeRetryAsync(delay, stoppingToken);
                delay = IncreaseDelay(delay);
            }
        }
    }

    private async Task ReadConnectionAsync(Stream pipe, CancellationToken cancellationToken)
    {
        var preamble = await ReadFrameAsync(pipe, cancellationToken);
        hub.AddDebug("message", "message.versionReceived", new { type = "Version" }, messageSize: preamble.Length, rawFrame: preamble);
        var version = PipeDecoder.DecodeFrame(preamble, true).Version ?? throw new ProtocolException("Versionsnachricht ohne Version.");
        hub.SetProtocolVersion(version);
        if (version != PipeDecoder.ProtocolVersion)
            throw new ProtocolException($"Nicht unterstützte Protokollversion: {version}.");

        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await ReadFrameAsync(pipe, cancellationToken);
            hub.Accept(PipeDecoder.DecodeFrame(frame), frame.Length, frame);
        }
    }

    private static async Task<byte[]> ReadFrameAsync(Stream pipe, CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[sizeof(int)];
        await ReadExactlyAsync(pipe, lengthBytes, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        PipeDecoder.ValidateMessageLength(length);
        var frame = new byte[length];
        await ReadExactlyAsync(pipe, frame, cancellationToken);
        return frame;
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (count == 0)
                throw new IOException("Die Pipe wurde während einer Nachricht geschlossen.");
            offset += count;
        }
    }

    private static TimeSpan IncreaseDelay(TimeSpan delay) => TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 15));
    private static Task WaitBeforeRetryAsync(TimeSpan delay, CancellationToken token) => Task.Delay(delay, token);

    private async Task HandleConnectionFailureAsync(Exception exception, string apiOperation, TimeSpan delay, CancellationToken token)
    {
        var failure = PipeConnectionErrors.Classify(exception, apiOperation);
        var error = new PipeErrorDetails(failure.Category, failure.Severity, failure.ApiOperation,
            failure.ErrorCode, failure.WindowsName, failure.EventKey, failure.Parameters);
        hub.SetPipeState(failure.Kind switch
        {
            PipeFailureKind.Timeout => PipeConnectionState.Timeout,
            PipeFailureKind.NotFound => PipeConnectionState.NotFound,
            PipeFailureKind.Busy => PipeConnectionState.Busy,
            _ => PipeConnectionState.Disconnected
        }, failure.EventKey, failure.EventKey, failure.Parameters);
        var signature = $"{failure.Kind}:{failure.ErrorCode}:{failure.ApiOperation}";
        if (!string.Equals(signature, _lastFailure, StringComparison.Ordinal))
        {
            _lastFailure = signature;
            hub.AddDebug("error", failure.EventKey, error: error);
        }
        hub.AddDebug("reconnect", "reconnect.scheduled", new { seconds = delay.TotalSeconds }, error: error);
        await WaitBeforeRetryAsync(delay, token);
    }
}