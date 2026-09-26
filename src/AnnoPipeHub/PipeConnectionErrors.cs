using System.ComponentModel;
using System.IO.Pipes;

namespace AnnoPipeHub;

public enum PipeFailureKind { NotFound, Busy, AccessDenied, Timeout, Unavailable, Other }
public sealed record PipeConnectionFailure(PipeFailureKind Kind, int ErrorCode, string WindowsName,
    string Category, string Severity, string ApiOperation, string EventKey, IReadOnlyDictionary<string, object?> Parameters);

public static class PipeConnectionErrors
{
    public static PipeConnectionFailure Classify(Exception exception, string apiOperation = "Unknown API operation")
    {
        var errorCode = GetErrorCode(exception);
        return errorCode switch
        {
            2 => Failure(PipeFailureKind.NotFound, errorCode, "ERROR_FILE_NOT_FOUND", "pipe.notFound", "pipe.notFound", apiOperation),
            231 => Failure(PipeFailureKind.Busy, errorCode, "ERROR_PIPE_BUSY", "pipe.busy", "pipe.busy", apiOperation),
            5 => Failure(PipeFailureKind.AccessDenied, errorCode, "ERROR_ACCESS_DENIED", "pipe.accessDenied", "pipe.accessDenied", apiOperation),
            1460 => Failure(PipeFailureKind.Timeout, errorCode, "ERROR_TIMEOUT", "pipe.timeout", "pipe.timeout", apiOperation),
            109 or 233 => Failure(PipeFailureKind.Unavailable, errorCode, errorCode == 109 ? "ERROR_BROKEN_PIPE" : "ERROR_PIPE_NOT_CONNECTED", "pipe.unavailable", "pipe.unavailable", apiOperation),
            _ => Failure(PipeFailureKind.Other, errorCode, "UNKNOWN", "pipe.unknown", "pipe.unknown", apiOperation)
        };
    }

    private static PipeConnectionFailure Failure(PipeFailureKind kind, int code, string name, string eventKey, string category, string operation) =>
        new(kind, code, name, category, kind is PipeFailureKind.Busy or PipeFailureKind.NotFound or PipeFailureKind.Timeout ? "WARN" : "ERROR", operation, eventKey,
            new Dictionary<string, object?> { ["windowsCode"] = code, ["windowsName"] = name, ["apiOperation"] = operation });

    public static int GetErrorCode(Exception exception)
    {
        if (exception is TimeoutException) return 1460;
        if (exception is Win32Exception win32) return win32.NativeErrorCode;
        if (exception is IOException or UnauthorizedAccessException)
            return exception.HResult & 0xFFFF;
        return exception.InnerException is null ? 0 : GetErrorCode(exception.InnerException);
    }
}
