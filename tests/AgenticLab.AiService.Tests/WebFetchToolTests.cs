using System.Net;
using AgenticLab.AiService.Application.Tools;
using Xunit;

namespace AgenticLab.AiService.Tests;

public sealed class WebFetchToolTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fd00:ec2::254")]
    [InlineData("::ffff:169.254.169.254")]
    public void InternalAddressesAreNotPublic(string address) =>
        Assert.False(WebFetchTool.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("93.184.215.14")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700::1111")]
    public void PublicAddressesArePublic(string address) =>
        Assert.True(WebFetchTool.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/iam/security-credentials/")]
    [InlineData("http://localhost:1/")]
    public async Task FetchingAnInternalAddressIsRefusedBeforeConnecting(string url)
    {
        using var http = new HttpClient(new SocketsHttpHandler { ConnectCallback = WebFetchTool.ConnectToPublicAddressAsync });

        var result = await new WebFetchTool(http).WebFetch(url);

        Assert.Contains("only public web pages can be fetched", result);
    }
}
