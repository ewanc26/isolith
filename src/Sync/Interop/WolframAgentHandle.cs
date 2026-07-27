using System;
using System.Runtime.InteropServices;

namespace Isolith.Sync.Interop;

/// <summary>
/// Owns a native <c>wf_agent *</c> and releases it through
/// <c>wf_agent_free</c>. No raw <see cref="IntPtr"/> escapes this layer, so an
/// agent cannot outlive its handle or be freed twice.
/// </summary>
internal sealed class WolframAgentHandle : SafeHandle
{
    private WolframAgentHandle() : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    internal WolframAgentHandle(IntPtr existing) : base(IntPtr.Zero, ownsHandle: true)
    {
        SetHandle(existing);
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        WolframNative.wf_agent_free(handle);
        return true;
    }
}
