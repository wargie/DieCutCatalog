using DieCutCatalog.Application.Security;

namespace DieCutCatalog.Infrastructure.Tests;

public sealed class ServerAddressPolicyTests
{
    [Theory]
    [InlineData("http://localhost:5080")]
    [InlineData("http://127.0.0.1:5080")]
    [InlineData("https://diecutcatalog.duckdns.org")]
    public void TryCreateBaseUri_AllowsSecureAndLocalAddresses(string address)
    {
        var accepted = ServerAddressPolicy.TryCreateBaseUri(address, false, out var uri, out var error);

        Assert.True(accepted);
        Assert.NotNull(uri);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("http://diecutcatalog.duckdns.org")]
    [InlineData("http://45.43.137.142:5080")]
    [InlineData("http://localhost.example.com")]
    [InlineData("http://127.0.0.2")]
    public void TryCreateBaseUri_RejectsRemoteHttp(string address)
    {
        var accepted = ServerAddressPolicy.TryCreateBaseUri(address, false, out var uri, out var error);

        Assert.False(accepted);
        Assert.Null(uri);
        Assert.Contains("HTTPS", error);
    }

    [Fact]
    public void TryCreateBaseUri_AllowsRemoteHttpOnlyWhenDevelopmentModeIsExplicit()
    {
        var accepted = ServerAddressPolicy.TryCreateBaseUri(
            "http://development-server.test:5080",
            true,
            out var uri,
            out var error);

        Assert.True(accepted);
        Assert.Equal("http://development-server.test:5080/", uri!.ToString());
        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("diecutcatalog.duckdns.org")]
    [InlineData("ftp://diecutcatalog.duckdns.org")]
    public void TryCreateBaseUri_RejectsInvalidOrUnsupportedAddresses(string address)
    {
        var accepted = ServerAddressPolicy.TryCreateBaseUri(address, false, out var uri, out var error);

        Assert.False(accepted);
        Assert.Null(uri);
        Assert.NotNull(error);
    }
}