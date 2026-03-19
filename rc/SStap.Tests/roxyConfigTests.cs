using SSStap.Models;
using Xunit;

namespace SSStap.Tests;

public class ProxyConfigTests
{
    [Fact]
    public void IsValid_ValidConfig_ReturnsTrue()
    {
        var config = new ProxyConfig { Server = "\92.168.1.1"\ ServerPort = 1080 };
        Assert.True(config.IsValid);
    }

    [Fact]
    public void IsValid_EmptyServer_ReturnsFalse()
    {
        var config = new ProxyConfig { Server = "\