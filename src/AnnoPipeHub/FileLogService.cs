using System.Threading.Channels;

namespace AnnoPipeHub;

public sealed class FileLogService : BackgroundService, IFileLogSink
{
    private const int QueueCapacity = 512;
    private const long MaxFileBytes = 5 * 1024 * 1024;
    private const int MaxFiles = 7;
    private readonly HubState _hub;
    private readonly string _directory;
    private readonly Channel<DebugLogEntry> _queue = Channel.CreateBounded<DebugLogEntry>(new BoundedChannelOptions(QueueCapacity)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly bool _rawLoggingEnabled;
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private volatile bool _enabled;
    private int _pendingWrites;
    private FileStream? _stream;
    private StreamWriter? _writer;
    private DateOnly? _date;
    private string? _path;
    private int _sequence;

    public FileLogService(HubState hub, string? directory = null, bool rawLoggingEnabled = false)
    {
        _hub = hub;
        _directory = directory ?? AppContext.BaseDirectory;
        _rawLoggingEnabled = rawLoggingEnabled;
        _hub.AttachFileLog(this);
    }

    public string DirectoryPath => _directory;
    public string? CurrentPath => _path;
    public bool Enabled => _enabled;
    public void Enqueue(DebugLogEntry entry)
    {
        if (!_enabled) return;
        Interlocked.Increment(ref _pendingWrites);
        if (!_queue.Writer.TryWrite(entry)) Interlocked.Decrement(ref _pendingWrites);
    }

    public async Task<FileLogStatus> EnableAsync(CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken);
        try
        {
            if (_enabled) return _hub.FileLog;
            _enabled = true;
            try
            {
                System.IO.Directory.CreateDirectory(_directory);
                await OpenAsync(DateOnly.FromDateTime(DateTime.Now), cancellationToken);
                return _hub.FileLog;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                _enabled = false;
                await DisableAsync(exception);
                return _hub.FileLog;
            }
        }
        finally { _ioGate.Release(); }
    }

    public async Task<FileLogStatus> DisableLoggingAsync(CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken);
        try
        {
            _enabled = false;
        }
        finally { _ioGate.Release(); }

        while (Volatile.Read(ref _pendingWrites) > 0)
            await Task.Delay(10, cancellationToken);

        await _ioGate.WaitAsync(cancellationToken);
        try
        {
            if (_writer is not null) await _writer.FlushAsync(cancellationToken);
            if (_writer is not null) await _writer.DisposeAsync();
            if (_stream is not null) await _stream.DisposeAsync();
            _writer = null;
            _stream = null;
            _hub.SetFileLogStatus(new FileLogStatus(false, false, _path, null));
            return _hub.FileLog;
        }
        finally { _ioGate.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _hub.SetFileLogStatus(new FileLogStatus(false, false, null, null));
            await foreach (var entry in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await WriteAsync(entry, stoppingToken);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    await DisableAsync(exception);
                }
                finally { Interlocked.Decrement(ref _pendingWrites); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            await DisableAsync(exception);
        }
        finally
        {
            if (_writer is not null) await _writer.DisposeAsync();
            if (_stream is not null) await _stream.DisposeAsync();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await DisableLoggingAsync(cancellationToken);
        _queue.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    private async Task WriteAsync(DebugLogEntry entry, CancellationToken token)
    {
        await _ioGate.WaitAsync(token);
        try
        {
            if (!_enabled) return;
            var date = DateOnly.FromDateTime(entry.AtUtc.LocalDateTime);
            if (_writer is null || _date != date || (_stream?.Length ?? 0) >= MaxFileBytes)
                await OpenAsync(date, token);

            await _writer!.WriteLineAsync(FormatLogLine(entry, _rawLoggingEnabled));
            await _writer.FlushAsync(token);
        }
        finally { _ioGate.Release(); }
    }

    public static string FormatLogLine(DebugLogEntry entry, bool rawLoggingEnabled = false)
    {
        var severity = entry.Error?.Severity ?? entry.Kind switch { "error" or "decoder" => "ERROR", "reconnect" => "WARN", _ => "INFO" };
        var line = entry.Error is { } error
            ? $"{entry.AtUtc:O} [{error.Severity}] [{error.Category.ToUpperInvariant()}] {error.EventKey} (Win32 {error.WindowsCode}: {error.WindowsName}; API {error.ApiOperation}; params={System.Text.Json.JsonSerializer.Serialize(error.Parameters)})"
            : $"{entry.AtUtc:O} [{severity}] [{entry.Kind.ToUpperInvariant()}] {entry.Message}";
        string? details = entry.Details is null ? null : entry.Details is string text ? text : System.Text.Json.JsonSerializer.Serialize(entry.Details);
        if (entry.MessageSize is not null) line += $" bytes={entry.MessageSize}";
        if (entry.MessageType is not null) line += $" type={entry.MessageType}";
        if (details is not null) line += $" details={details}";
        if (rawLoggingEnabled && entry.RawHex is not null) line += $" rawHex={entry.RawHex}";
        return line;
    }

    private async Task OpenAsync(DateOnly date, CancellationToken token)
    {
        if (_writer is not null) await _writer.DisposeAsync();
        if (_stream is not null) await _stream.DisposeAsync();
        _date = date;
        _sequence = 0;
        var basePath = GetLogPath(_directory, date);
        _path = basePath;
        while (File.Exists(_path) && new FileInfo(_path).Length >= MaxFileBytes)
        {
            _sequence++;
            _path = GetLogPath(_directory, date, _sequence);
        }
        _stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
        _writer = new StreamWriter(_stream) { AutoFlush = false };
        _hub.SetFileLogStatus(new FileLogStatus(true, true, _path, null));
        CleanupOldFiles();
        await _writer.FlushAsync(token);
    }

    private async Task DisableAsync(Exception exception)
    {
        var error = new FileLogStatus(_enabled, false, _path, "filelog.unavailable");
        _hub.SetFileLogStatus(error);
        _hub.AddDebug("error", "filelog.unavailable", new { detail = exception.Message });
        _queue.Writer.TryComplete();
        await Task.CompletedTask;
    }

    private void CleanupOldFiles()
    {
        var files = System.IO.Directory.EnumerateFiles(_directory, "Anno117PipeHub-*.log")
            .Select(path => new FileInfo(path)).OrderByDescending(file => file.LastWriteTimeUtc).Skip(MaxFiles).ToArray();
        foreach (var file in files)
        {
            try { file.Delete(); } catch { }
        }
    }

    public static string GetLogPath(string directory, DateOnly date, int sequence = 0) => Path.Combine(directory,
        sequence == 0 ? $"Anno117PipeHub-{date:yyyy-MM-dd}.log" : $"Anno117PipeHub-{date:yyyy-MM-dd}-{sequence:00}.log");
}
