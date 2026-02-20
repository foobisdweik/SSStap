using System.IO;
using System.Text.Json;
using SSStap.Models;
using SSStap.Services;
using Xunit;

namespace SSStap.Tests;

public class ConfigServiceTests
{
    private readonly string _tempDir;

    public ConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SSStap.Tests." + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void LoadProxyList_EmptyFile_ReturnsEmptyList()
    {
        var svc = new ConfigService(_tempDir);
        var result = svc.LoadProxyList();
        Assert.NotNull(result);
        Assert.Empty(result.Configs);
        Assert.Equal(0, result.IdInUse);
    }

    [Fact]
    public void LoadProxyList_MissingFile_ReturnsEmptyList()
    {
        var emptyDir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyDir);
        var svc = new ConfigService(emptyDir);
        var result = svc.LoadProxyList();
        Assert.NotNull(result);
        Assert.Empty(result.Configs);
    }

    [Fact]
    public void LoadProxyList_LegacyFormat_DeserializesCorrectly()
    {
        var legacy = """{"configs":[{"server":"172.20.10.1","server_port":9999,"type":5,"method":"none","password":"","remarks":"","group":"Default Group"}],"idInUse":0}""";
        File.WriteAllText(Path.Combine(_tempDir, "proxylist.json"), legacy);
        var svc = new ConfigService(_tempDir);
        var result = svc.LoadProxyList();
        Assert.Single(result.Configs);
        var c = result.Configs[0];
        Assert.Equal("172.20.10.1", c.Server);
        Assert.Equal(9999, c.ServerPort);
        Assert.Equal(5, c.Type);
        Assert.Equal("Default Group", c.Group);
    }

    [Fact]
    public void SaveProxyList_RoundTrip_PreservesData()
    {
        var svc = new ConfigService(_tempDir);
        var list = new ProxyListJson
        {
            Configs =
            [
                new ProxyConfig
                {
                    Server = "10.0.0.1",
                    ServerPort = 1080,
                    Type = 5,
                    Remarks = "Test",
                    Group = "Test Group"
                }
            ],
            IdInUse = 0
        };
        svc.SaveProxyList(list);
        var loaded = svc.LoadProxyList();
        Assert.Single(loaded.Configs);
        Assert.Equal("10.0.0.1", loaded.Configs[0].Server);
        Assert.Equal(1080, loaded.Configs[0].ServerPort);
        Assert.Equal("Test", loaded.Configs[0].Remarks);
    }

    [Fact]
    public void LoadAppConfig_MissingFile_ReturnsDefaults()
    {
        var svc = new ConfigService(_tempDir);
        var config = svc.LoadAppConfig();
        Assert.NotNull(config);
        Assert.Equal("http://global.bing.com", config.TestUrl);
        Assert.Equal(10, config.DelayedConnect);
    }

    [Fact]
    public void LoadAppConfig_WithValues_ParsesCorrectly()
    {
        var ini = @"[basic]
last_proxymode_index=2
testurl=http://example.com
DelayedConnect=5
";
        File.WriteAllText(Path.Combine(_tempDir, "config.ini"), ini);
        var svc = new ConfigService(_tempDir);
        var config = svc.LoadAppConfig();
        Assert.Equal(2, config.LastProxyModeIndex);
        Assert.Equal("http://example.com", config.TestUrl);
        Assert.Equal(5, config.DelayedConnect);
    }
}
