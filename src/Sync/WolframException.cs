using System;

namespace Isolith.Sync;

/// <summary>
/// Raised when libwolfram returns a non-<see cref="WolframStatus.Ok"/> status.
/// The SDK's own status is preserved rather than collapsed into a generic
/// failure, so callers can react to (say) a rate limit differently from a
/// validation error.
/// </summary>
public sealed class WolframException : Exception
{
    public WolframException(WolframStatus status, string? operation = null)
        : base(BuildMessage(status, operation))
    {
        Status = status;
        Operation = operation;
    }

    /// <summary>The status libwolfram returned.</summary>
    public WolframStatus Status { get; }

    /// <summary>The SDK call that failed, when the caller supplied it.</summary>
    public string? Operation { get; }

    private static string BuildMessage(WolframStatus status, string? operation)
    {
        string described = WolframStatusText.Describe(status);
        return operation is null ? described : $"{operation}: {described}";
    }

    /// <summary>Throws if <paramref name="status"/> is not <see cref="WolframStatus.Ok"/>.</summary>
    public static void ThrowIfFailed(int status, string operation)
    {
        var typed = (WolframStatus)status;
        if (typed != WolframStatus.Ok)
            throw new WolframException(typed, operation);
    }
}
