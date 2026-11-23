using NexaDock.Core.Updates;

namespace NexaDock.Tests;

public class UpdateRulesTests
{
    [Theory]
    [InlineData("v0.4.0", "0.4.0")]
    [InlineData("0.4.1", "0.4.1")]
    [InlineData("v1.2", "1.2.0")]
    [InlineData(" V2.0.3 ", "2.0.3")]
    public void Release_tags_become_versions(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateRules.ParseTag(tag));
    }

    [Theory]
    [InlineData("v0.5.0-beta")]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData(null)]
    public void Pre_releases_and_other_tags_are_ignored(string? tag)
    {
        Assert.Null(UpdateRules.ParseTag(tag));
    }

    [Fact]
    public void Only_a_higher_version_is_newer()
    {
        Assert.True(UpdateRules.IsNewer(new Version(0, 4, 1), new Version(0, 4, 0)));
        Assert.True(UpdateRules.IsNewer(new Version(1, 0), new Version(0, 9, 9)));
        Assert.False(UpdateRules.IsNewer(new Version(0, 4), new Version(0, 4, 0)));
        Assert.False(UpdateRules.IsNewer(new Version(0, 3, 5), new Version(0, 4, 0)));
    }

    [Fact]
    public void The_running_version_parses()
    {
        Assert.True(UpdateRules.Current > new Version(0, 0, 0));
    }
}
