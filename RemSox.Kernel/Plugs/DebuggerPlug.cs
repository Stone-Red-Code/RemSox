using Cosmos.Build.API.Attributes;

namespace RemSox.Kernel.Plugs;

/// <summary>
/// Cosmos does not provide the RhDebugBreak runtime import behind <see cref="System.Diagnostics.Debugger.Break"/>,
/// so any reachable call fails to link. There is no debugger to break into, so Break is a no-op.
/// </summary>
[Plug(typeof(System.Diagnostics.Debugger))]
public static class DebuggerPlug
{
    [PlugMember]
    public static void Break()
    {
    }
}
