using AgenticLab.ParameterStore;
using AgenticLab.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AgenticLab.Web.Tests;

public sealed class PathBaseTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("/", "")]
    [InlineData(" /agentic-lab/ ", "/agentic-lab")]
    public void ReadsANormalisedPrefix(string? value, string expected) =>
        Assert.Equal(expected, PathBase.Read(Configuration(PathBase.ConfigurationKey, value)).Value ?? "");

    [Fact]
    public void ARelativePrefixIsRejected() =>
        Assert.Throws<InvalidOperationException>(() => PathBase.Read(Configuration(PathBase.ConfigurationKey, "agentic-lab")));

    [Theory]
    // The shared ALB strips the prefix before forwarding; the app still needs it for links and redirect URIs.
    [InlineData("/learn", "/learn")]
    [InlineData("/", "/")]
    // Without a stripping proxy (e.g. local runs) the prefix arrives in the path and moves to the path base.
    [InlineData("/agentic-lab/learn", "/learn")]
    [InlineData("/Agentic-Lab/learn", "/learn")]
    public void TheRequestAlwaysCarriesThePrefixAsItsPathBase(string path, string expectedPath)
    {
        var request = new DefaultHttpContext().Request;
        request.Path = path;

        PathBase.Apply(request, "/agentic-lab");

        Assert.Equal("/agentic-lab", request.PathBase.Value, ignoreCase: true);
        Assert.Equal(expectedPath, request.Path.Value);
    }

    [Fact]
    public void ParameterStoreIsSkippedWithoutAPath()
    {
        var builder = WebApplication.CreateBuilder();
        var sources = builder.Configuration.Sources.Count;

        builder.AddParameterStore();

        Assert.Equal(sources, builder.Configuration.Sources.Count);
    }

    [Fact]
    public void ARelativeParameterStorePathIsRejected()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration[ParameterStoreConfiguration.PathKey] = "agentic-lab/sandbox";

        Assert.Throws<InvalidOperationException>(() => builder.AddParameterStore());
    }

    private static IConfiguration Configuration(string key, string? value) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();
}
