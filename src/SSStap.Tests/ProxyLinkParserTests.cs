using SSStap.Models;
using SSStap.Services;
using Xunit;

namespace SSStap.Tests;

public class ProxyLinkParserTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://example.com")]
    public void Parse_InvalidInput_ReturnsNull(string input)
    {
        Assert.Null(ProxyLinkParser.Parse(input));
    }

    [Fact]
    public void Parse_NullInput_ReturnsNull()
    {
        Assert.Null(ProxyLinkParser.Parse(null!));
    }

    [Fact]
    public void Parse_SsLink_PlainFormat()
    {
        // ss://base64(method:password)@host:port#tag
        // aes-128-gcm:secret@example.com:8388#MyServer
        // base64: YWVzLTEyOC1nY206c2VjcmV0
        var link = "ss://YWVzLTEyOC1nY206c2VjcmV0@example.com:8388#MyServer";
        var config = ProxyLinkParser.Parse(link);
        Assert.NotNull(config);
        Assert.Equal("example.com", config.Server);
        Assert.Equal(8388, config.ServerPort);
        Assert.Equal("aes-128-gcm", config.Method);
        Assert.Equal("secret", config.Password);
        Assert.Equal("MyServer", config.Remarks);
        Assert.Equal((int)ProxyType.Shadowsocks, config.Type);
    }

    [Fact]
    public void Parse_SsLink_NoTag()
    {
        var link = "ss://YWVzLTEyOC1nY206c2VjcmV0@192.168.1.1:1080";
        var config = ProxyLinkParser.Parse(link);
        Assert.NotNull(config);
        Assert.Equal("192.168.1.1", config.Server);
        Assert.Equal(1080, config.ServerPort);
        Assert.Equal("", config.Remarks);
    }

    [Fact]
    public void Parse_SsrLink()
    {
        // ssr://host:port:protocol:method:obfs:base64(password)/?params
        // Minimal: host:port:origin:aes-128-cfb:plain:password
        var encoded = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("example.com:8388:origin:aes-128-cfb:plain:dGVzdA=="))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var link = $"ssr://{encoded}";
        var config = ProxyLinkParser.Parse(link);
        Assert.NotNull(config);
        Assert.Equal("example.com", config.Server);
        Assert.Equal(8388, config.ServerPort);
        Assert.Equal("origin", config.Protocol);
        Assert.Equal("aes-128-cfb", config.Method);
        Assert.Equal("plain", config.Obfs);
        Assert.Equal((int)ProxyType.ShadowsocksR, config.Type);
    }

    [Fact]
    public void Parse_SsLink_Sip002_Base64Url()
    {
        // SIP002: ss://base64url(method:password)@host:port#tag
        var userInfo = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("chacha20-ietf-poly1305:mypassword"))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var link = $"ss://{userInfo}@server.com:443#SIP002";
        var config = ProxyLinkParser.Parse(link);
        Assert.NotNull(config);
        Assert.Equal("server.com", config.Server);
        Assert.Equal(443, config.ServerPort);
        Assert.Equal("chacha20-ietf-poly1305", config.Method);
        Assert.Equal("mypassword", config.Password);
        Assert.Equal("SIP002", config.Remarks);
    }
}
