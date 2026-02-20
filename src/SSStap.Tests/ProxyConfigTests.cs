using SSStap.Models;
using Xunit;

namespace SSStap.Tests;

public class ProxyConfigTests
{
    [Fact]
    public void IsValid_ValidConfig_ReturnsTrue()
    {
        var config = new ProxyConfig { Server = "192.168.1.1", ServerPort = 1080 };
        Assert.True(config.IsValid);
    }

    [Fact]
    public void IsValid_EmptyServer_ReturnsFalse()
    {
        var config = new ProxyConfig { Server = "", ServerPort = 1080 };
        Assert.False(config.IsValid);
    }

    [Fact]
    public void IsValid_WhitespaceServer_ReturnsFalse()
    {
        var config = new ProxyConfig { Server = "   ", ServerPort = 1080 };
        Assert.False(config.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(99999)]
    public void IsValid_InvalidPort_ReturnsFalse(int port)
    {
        var config = new ProxyConfig { Server = "example.com", ServerPort = port };
        Assert.False(config.IsValid);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(80)]
    [InlineData(1080)]
    [InlineData(65535)]
    public void IsValid_ValidPort_ReturnsTrue(int port)
    {
        var config = new ProxyConfig { Server = "example.com", ServerPort = port };
        Assert.True(config.IsValid);
    }

    [Fact]
    public void DisplayName_WithRemarks_ReturnsRemarks()
    {
        var config = new ProxyConfig { Server = "example.com", ServerPort = 1080, Remarks = "My Server" };
        Assert.Equal("My Server", config.DisplayName);
    }

    [Fact]
    public void DisplayName_WithoutRemarks_ReturnsServerAndPort()
    {
        var config = new ProxyConfig { Server = "example.com", ServerPort = 1080, Remarks = "" };
        Assert.Equal("example.com:1080", config.DisplayName);
    }
}
