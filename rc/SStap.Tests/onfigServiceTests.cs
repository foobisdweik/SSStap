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
        _tempDir = Path.Combine(Path.GetTempPath(), "\SStap.Tests."\+ Guid.NewGuid().ToString("\"\[..8]);
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
        var emptyDir = Path.Combine(_tempDir, "\mpty"\;
        Directory.CreateDirectory(emptyDir);
        var svc = new ConfigService(emptyDir);
        var result = svc.LoadProxyList();
        Assert.NotNull(result);
        Assert.Empty(result.Configs);
    }

    [Fact]
    public void LoadProxyList_LegacyFormat_DeserializesCorrectly()
    {
        var legacy = "\