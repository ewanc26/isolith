using System;
using System.Runtime.InteropServices;
using Isolith.Sync.Interop;

namespace Isolith.Sync;

/// <summary>An <c>at://</c> URI plus the CID of the record it points at.</summary>
public readonly record struct RecordRef(string Uri, string Cid)
{
    public override string ToString() => Uri;
}

/// <summary>
/// Managed, idiomatic wrapper over a libwolfram agent. Every method forwards to
/// the C SDK — session handling, XRPC transport, DAG-CBOR and record writes all
/// happen in native code, not here.
/// </summary>
/// <remarks>
/// <b>These calls block.</b> They perform real network I/O, so they must never
/// run on Godot's main thread; <see cref="AtprotoService"/> is the async front
/// end that keeps the frame loop free.
///
/// A single agent is not assumed to be safe for concurrent native calls, so
/// each one is serialised on <see cref="_gate"/>.
/// </remarks>
public sealed class WolframAgent : IDisposable
{
    private readonly WolframAgentHandle _handle;
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>Creates an agent bound to a PDS or entryway base URL.</summary>
    /// <param name="serviceUrl">e.g. <c>https://bsky.social</c>.</param>
    public WolframAgent(string serviceUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceUrl);

        WolframNative.Init();
        ServiceUrl = serviceUrl;

        IntPtr ptr = WolframNative.wf_agent_new(serviceUrl);
        if (ptr == IntPtr.Zero)
            throw new WolframException(WolframStatus.ErrAlloc, nameof(WolframNative.wf_agent_new));

        _handle = new WolframAgentHandle(ptr);
    }

    /// <summary>The base URL this agent was constructed with.</summary>
    public string ServiceUrl { get; }

    /// <summary>The signed-in DID, or <c>null</c> before a successful login.</summary>
    public string? Did => BorrowedString(WolframNative.wf_agent_get_did);

    /// <summary>The signed-in handle, or <c>null</c> before a successful login.</summary>
    public string? Handle => BorrowedString(WolframNative.wf_agent_get_handle);

    /// <summary>True once <see cref="Login"/> has succeeded.</summary>
    public bool IsAuthenticated => !string.IsNullOrEmpty(Did);

    /// <summary>
    /// Creates a session via <c>com.atproto.server.createSession</c>.
    /// </summary>
    /// <param name="identifier">A handle, DID, or email.</param>
    /// <param name="appPassword">
    /// An app password. Never pass an account's main password — app passwords
    /// are revocable per-app and are what this flow expects.
    /// </param>
    public void Login(string identifier, string appPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(appPassword);

        Invoke(ptr => WolframNative.wf_agent_login(ptr, identifier, appPassword), "wf_agent_login");
    }

    /// <summary>Ends the session via <c>com.atproto.server.deleteSession</c>.</summary>
    public void Logout() => Invoke(WolframNative.wf_agent_logout, "wf_agent_logout");

    /// <summary>
    /// Writes a record into the signed-in repo, minting a monotonic TID record
    /// key natively.
    /// </summary>
    /// <param name="collection">The record's NSID, e.g. <c>uk.ewancroft.platformer.run</c>.</param>
    /// <param name="recordJson">The record body as JSON, including its <c>$type</c>.</param>
    public RecordRef CreateRecord(string collection, string recordJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordJson);

        lock (_gate)
        {
            ThrowIfDisposed();
            using var scope = new HandleScope(_handle);

            int status = WolframNative.wf_agent_create_record_with_tid(
                scope.Pointer, collection, recordJson, out WolframNative.PostResult result);

            try
            {
                WolframException.ThrowIfFailed(status, "wf_agent_create_record_with_tid");
                return new RecordRef(
                    Marshal.PtrToStringUTF8(result.Uri) ?? string.Empty,
                    Marshal.PtrToStringUTF8(result.Cid) ?? string.Empty);
            }
            finally
            {
                WolframNative.wf_agent_post_result_free(ref result);
            }
        }
    }

    /// <summary>
    /// Lists records from a collection in the signed-in repo
    /// (<c>com.atproto.repo.listRecords</c>), returning the raw JSON body so
    /// callers can decode exactly the fields they need.
    /// </summary>
    public string ListRecords(string collection, int limit = 50, string? cursor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        return WithResponse(
            (IntPtr ptr, out WolframNative.Response res) =>
                WolframNative.wf_agent_list_records(ptr, collection, limit, cursor, out res),
            "wf_agent_list_records");
    }

    /// <summary>
    /// Fetches a single record by key (<c>com.atproto.repo.getRecord</c>),
    /// returning the raw JSON body.
    /// </summary>
    public string GetRecord(string collection, string rkey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(rkey);

        return WithResponse(
            (IntPtr ptr, out WolframNative.Response res) =>
                WolframNative.wf_agent_get_record(ptr, collection, rkey, out res),
            "wf_agent_get_record");
    }

    /// <summary>Deletes a record by key.</summary>
    public void DeleteRecord(string collection, string rkey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(rkey);

        Invoke(ptr => WolframNative.wf_agent_delete_record(ptr, collection, rkey), "wf_agent_delete_record");
    }

    /// <summary>
    /// Publishes an <c>app.bsky.feed.post</c>. Used only by the explicit
    /// "share this run" action — finishing a course never posts on its own.
    /// </summary>
    public RecordRef Post(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        lock (_gate)
        {
            ThrowIfDisposed();
            using var scope = new HandleScope(_handle);

            int status = WolframNative.wf_agent_post(scope.Pointer, text, out WolframNative.PostResult result);
            try
            {
                WolframException.ThrowIfFailed(status, "wf_agent_post");
                return new RecordRef(
                    Marshal.PtrToStringUTF8(result.Uri) ?? string.Empty,
                    Marshal.PtrToStringUTF8(result.Cid) ?? string.Empty);
            }
            finally
            {
                WolframNative.wf_agent_post_result_free(ref result);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _handle.Dispose();
        }
    }

    // -----------------------------------------------------------------------
    // Call plumbing
    // -----------------------------------------------------------------------

    private delegate int ResponseCall(IntPtr agent, out WolframNative.Response response);

    private string WithResponse(ResponseCall call, string operation)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var scope = new HandleScope(_handle);

            int status = call(scope.Pointer, out WolframNative.Response response);
            try
            {
                WolframException.ThrowIfFailed(status, operation);

                if (response.Body == IntPtr.Zero || response.BodyLen == 0)
                    return string.Empty;

                // Length-delimited: the SDK reports body_len, and the body is
                // not guaranteed to be NUL-terminated.
                return Marshal.PtrToStringUTF8(response.Body, checked((int)response.BodyLen));
            }
            finally
            {
                WolframNative.wf_response_free(ref response);
            }
        }
    }

    private void Invoke(Func<IntPtr, int> call, string operation)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var scope = new HandleScope(_handle);
            WolframException.ThrowIfFailed(call(scope.Pointer), operation);
        }
    }

    private string? BorrowedString(Func<IntPtr, IntPtr> accessor)
    {
        lock (_gate)
        {
            if (_disposed)
                return null;

            using var scope = new HandleScope(_handle);
            IntPtr native = accessor(scope.Pointer);

            // Borrowed from the agent — copied out, never freed here.
            return native == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(native);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>
    /// Keeps the <see cref="SafeHandle"/> ref-counted for the duration of a
    /// native call, so the agent cannot be finalised mid-request.
    /// </summary>
    private ref struct HandleScope
    {
        private readonly WolframAgentHandle _handle;
        private bool _acquired;

        internal HandleScope(WolframAgentHandle handle)
        {
            _handle = handle;
            _acquired = false;
            handle.DangerousAddRef(ref _acquired);
            Pointer = handle.DangerousGetHandle();
        }

        internal IntPtr Pointer { get; }

        public void Dispose()
        {
            if (_acquired)
                _handle.DangerousRelease();
        }
    }
}
