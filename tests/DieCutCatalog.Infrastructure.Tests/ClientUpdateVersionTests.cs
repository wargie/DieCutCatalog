using DieCutCatalog.Application.Updates;

namespace DieCutCatalog.Infrastructure.Tests;

public sealed class ClientUpdateVersionTests
{
    [Theory]
    [InlineData("1.1.2", "1.1.1")]
    [InlineData("v2.0.0", "1.9.9")]
    [InlineData("1.2.0+build.4", "1.1.9")]
    public void IsNewer_AcceptsNewerSemanticVersion(string candidate, string current)
    {
        Assert.True(ClientUpdateVersion.IsNewer(candidate, current));
    }

    [Theory]
    [InlineData("1.1.1", "1.1.1")]
    [InlineData("1.1.0", "1.1.1")]
    [InlineData("invalid", "1.1.1")]
    [InlineData(null, "1.1.1")]
    public void IsNewer_RejectsCurrentOlderOrInvalidVersion(string? candidate, string current)
    {
        Assert.False(ClientUpdateVersion.IsNewer(candidate, current));
    }
}