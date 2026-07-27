namespace Isolith.Sync;

/// <summary>
/// Managed mirror of libwolfram's <c>wf_status</c> enum (canonical in
/// <c>include/wolfram/xrpc.h</c>). Values are forwarded from the SDK verbatim —
/// the game never invents or remaps a status.
/// </summary>
public enum WolframStatus
{
    Ok = 0,
    ErrInvalidArg = 1,
    ErrAlloc = 2,
    ErrNetwork = 3,
    ErrHttp = 4,
    ErrParse = 5,
    ErrNotFound = 6,
    ErrWouldBlock = 7,
    ErrDidResolve = 8,
    ErrDidDocumentNotFound = 9,
    ErrHandleResolve = 10,
    ErrHandleDocumentNotFound = 11,
    ErrHandleTtlExpired = 12,
    ErrHandleCacheKey = 13,
    ErrCrypto = 14,
    ErrValidation = 15,
    ErrState = 16,
    ErrConfig = 17,
    ErrTimeout = 18,
    ErrUnsupported = 19,
    ErrPermission = 20,
    ErrRateLimit = 21,
    ErrDuplicate = 22,
    ErrConflict = 23,
    ErrNotImplemented = 24,
    ErrInternal = 25,
    ErrUnknown = 26,
}

/// <summary>Human-readable text for <see cref="WolframStatus"/> values shown in the UI.</summary>
public static class WolframStatusText
{
    public static string Describe(WolframStatus status) => status switch
    {
        WolframStatus.Ok => "OK",
        WolframStatus.ErrNetwork => "Could not reach the server. Check your connection and PDS URL.",
        WolframStatus.ErrHttp => "The server rejected the request.",
        WolframStatus.ErrTimeout => "The server took too long to respond.",
        WolframStatus.ErrPermission => "Not authorised — check your handle and app password.",
        WolframStatus.ErrValidation => "The record did not validate against its lexicon.",
        WolframStatus.ErrRateLimit => "Rate limited by the server. Try again shortly.",
        WolframStatus.ErrHandleResolve => "That handle could not be resolved.",
        WolframStatus.ErrDidResolve => "That DID could not be resolved.",
        WolframStatus.ErrState => "No active session — sign in first.",
        WolframStatus.ErrNotImplemented => "libwolfram reports this endpoint is not implemented.",
        _ => $"libwolfram error: {status}",
    };
}
