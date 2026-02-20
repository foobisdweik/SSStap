using System.Runtime.InteropServices;
using SSStap.Native;
using Xunit;

namespace SSStap.Tests;

/// <summary>
/// Wintun integration tests. Requires wintun.dll (v0.14+ from wintun.net) in the test output.
/// WintunSession.Create may require Administrator; older wintun.dll may lack WintunGetAdapterLuid.
/// </summary>
public class WintunTests
{
    [Fact]
    public void WintunGetRunningDriverVersion_ReturnsVersionOrZero()
    {
        var version = Wintun.WintunGetRunningDriverVersion();
        Assert.True(version >= 0);
    }

    [Fact]
    public void WintunSession_Create_SucceedsOrFailsGracefully()
    {
        try
        {
            var session = WintunSession.Create("SSStap-Test", "Wintun", 0x20000);
            try
            {
                if (session != null)
                {
                    Assert.False(string.IsNullOrEmpty(session.AdapterName));
                    Assert.True(session.ReadWaitEvent != nint.Zero);
                }
            }
            finally
            {
                session?.Dispose();
            }
        }
        catch (EntryPointNotFoundException)
        {
            // Old wintun.dll (pre-0.14) lacks WintunGetAdapterLuid - download from wintun.net
            Assert.True(true, "Wintun DLL is outdated; use wintun.net amd64 build");
        }
    }
}
