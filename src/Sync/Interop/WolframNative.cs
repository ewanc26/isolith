using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Isolith.Sync.Interop;

/// <summary>
/// Raw P/Invoke tier over <c>libwolfram</c>'s C11 ABI — a 1:1 mirror of the
/// declarations in <c>include/wolfram/agent.h</c> and <c>xrpc.h</c>. No logic
/// lives here; every member is a direct pass-through.
/// </summary>
/// <remarks>
/// Conventions follow the upstream <c>Wolfram.Interop</c> raw tier:
/// source-generated <c>LibraryImport</c> (trim/NativeAOT safe), explicit UTF-8
/// string marshalling, <c>nuint</c> for <c>size_t</c>, <c>CLong</c> for C
/// <c>long</c>, and opaque handles as <see cref="IntPtr"/> with ownership
/// handled one tier up by a <see cref="System.Runtime.InteropServices.SafeHandle"/>.
///
/// Strings the SDK returns fall into two classes, and they are marshalled
/// differently on purpose:
/// <list type="bullet">
///   <item><b>Borrowed</b> (<c>wf_agent_get_did</c>, <c>wf_agent_get_handle</c>)
///   — owned by the agent, must never be freed. Returned as <see cref="IntPtr"/>
///   and copied with <c>Marshal.PtrToStringUTF8</c>.</item>
///   <item><b>Owned</b> (fields of <c>wf_agent_post_result</c> and
///   <c>wf_response</c>) — released by the SDK's own
///   <c>*_free</c> functions, never by the marshaller.</item>
/// </list>
/// </remarks>
internal static unsafe partial class WolframNative
{
    static WolframNative() => WolframLibrary.EnsureRegistered();

    /// <summary>
    /// Forces the static constructor to run so the resolver is installed before
    /// the first native call.
    /// </summary>
    internal static void Init() => RuntimeHelpers.RunClassConstructor(typeof(WolframNative).TypeHandle);

    // ---------------------------------------------------------------------
    // Structs (layout mirrors the C declarations exactly)
    // ---------------------------------------------------------------------

    /// <summary>Mirrors <c>wf_agent_post_result</c>. Release with <see cref="wf_agent_post_result_free"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PostResult
    {
        /// <summary>Owned UTF-8 <c>at://</c> URI of the created record.</summary>
        public IntPtr Uri;

        /// <summary>Owned UTF-8 CID of the created record.</summary>
        public IntPtr Cid;
    }

    /// <summary>
    /// Mirrors <c>wf_response</c>. Release with <see cref="wf_response_free"/>.
    /// <c>status</c> is a C <c>long</c>, which is 8 bytes on Unix and 4 on
    /// Windows — <see cref="CLong"/> tracks that per-platform.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Response
    {
        public CLong Status;
        public IntPtr Body;
        public nuint BodyLen;
        public IntPtr DpopNonce;
    }

    // ---------------------------------------------------------------------
    // Agent lifecycle
    // ---------------------------------------------------------------------

    [LibraryImport(WolframLibrary.ImportName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr wf_agent_new(string serviceUrl);

    [LibraryImport(WolframLibrary.ImportName)]
    internal static partial void wf_agent_free(IntPtr agent);

    // ---------------------------------------------------------------------
    // Session
    // ---------------------------------------------------------------------

    [LibraryImport(WolframLibrary.ImportName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int wf_agent_login(IntPtr agent, string identifier, string password);

    [LibraryImport(WolframLibrary.ImportName)]
    internal static partial int wf_agent_logout(IntPtr agent);

    /// <summary>Borrowed UTF-8 DID; do not free.</summary>
    [LibraryImport(WolframLibrary.ImportName)]
    internal static partial IntPtr wf_agent_get_did(IntPtr agent);

    /// <summary>Borrowed UTF-8 handle; do not free.</summary>
    [LibraryImport(WolframLibrary.ImportName)]
    internal static partial IntPtr wf_agent_get_handle(IntPtr agent);

    // ---------------------------------------------------------------------
    // Repo records
    // ---------------------------------------------------------------------

    /// <summary>
    /// <c>com.atproto.repo.putRecord</c> with a freshly minted monotonic TID
    /// record key, so the caller never has to invent one.
    /// </summary>
    [LibraryImport(WolframLibrary.ImportName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int wf_agent_create_record_with_tid(
        IntPtr agent, string collection, string recordJson, out PostResult result);

    [LibraryImport(WolframLibrary.ImportName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int wf_agent_create_record(
        IntPtr agent, string collection, string recordJson, out PostResult result);

    [LibraryImport(WolframLibrary.ImportName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int wf_agent_get_record(
        IntPtr agent, string collection, string rkey, out Response response);

    [LibraryImport(WolframLibrary.ImportName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int wf_agent_list_records(
        IntPtr agent, string collection, int limit,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? cursor, out Response response);

    [LibraryImport(WolframLibrary.ImportName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int wf_agent_delete_record(IntPtr agent, string collection, string rkey);

    [LibraryImport(WolframLibrary.ImportName)]
    internal static partial void wf_agent_post_result_free(ref PostResult result);

    [LibraryImport(WolframLibrary.ImportName)]
    internal static partial void wf_response_free(ref Response response);

    // ---------------------------------------------------------------------
    // Posts (used for the optional "share your run" post)
    // ---------------------------------------------------------------------

    [LibraryImport(WolframLibrary.ImportName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int wf_agent_post(IntPtr agent, string text, out PostResult result);
}
