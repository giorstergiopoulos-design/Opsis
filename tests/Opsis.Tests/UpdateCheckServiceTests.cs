using MotionDesk.Services;
using Xunit;

namespace Opsis.Tests;

// GearWin self-update port: οι καθαρά λογικές συναρτήσεις (χωρίς δίκτυο) — trusted-URL check,
// digest parsing, tag->Version, και το πλήρες Parse() πάνω σε συνθετικό JSON ίδιου σχήματος με
// αυτό που επιστρέφει το πραγματικό GitHub releases API.
public sealed class UpdateCheckServiceTests
{
    [Theory]
    [InlineData("https://github.com/giorstergiopoulos-design/Opsis/releases/download/v1.9.12/OpsisSetup.exe", true)]
    [InlineData("https://github.com/someone-else/Opsis/releases/download/v1.9.12/OpsisSetup.exe", false)]
    [InlineData("https://evil.com/github.com/giorstergiopoulos-design/Opsis/releases/download/v1.9.12/OpsisSetup.exe", false)]
    [InlineData("http://github.com/giorstergiopoulos-design/Opsis/releases/download/v1.9.12/OpsisSetup.exe", false)] // http, not https
    [InlineData("https://github.com/giorstergiopoulos-design/Opsis/releases/latest", false)] // right host, wrong path shape
    [InlineData(null, false)]
    public void IsTrustedDownloadUrl_OnlyAcceptsThisRepositorysReleaseDownloads(string? url, bool expected) =>
        Assert.Equal(expected, UpdateCheckService.IsTrustedDownloadUrl(url));

    [Fact]
    public void ParseDigest_Valid64HexSha256_ReturnsLowercase()
    {
        var hex = new string('A', 64);
        var result = UpdateCheckService.ParseDigest("sha256:" + hex);
        Assert.Equal(new string('a', 64), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("md5:aabbcc")] // wrong algorithm prefix
    [InlineData("sha256:tooshort")]
    [InlineData("sha256:")]
    public void ParseDigest_InvalidInput_ReturnsNull(string? digest) =>
        Assert.Null(UpdateCheckService.ParseDigest(digest));

    [Theory]
    [InlineData("v1.9.12", true, 1, 9, 12)]
    [InlineData("1.9.12", true, 1, 9, 12)]
    [InlineData("V2.0.0", true, 2, 0, 0)]
    [InlineData("v1.9.12-beta", true, 1, 9, 12)] // pre-release suffix stripped
    [InlineData("not-a-version", false, 0, 0, 0)]
    public void TryParseVersion_HandlesTagFormats(string tag, bool expectSuccess, int major, int minor, int build)
    {
        bool ok = UpdateCheckService.TryParseVersion(tag, out var version);
        Assert.Equal(expectSuccess, ok);
        if (expectSuccess) Assert.Equal(new Version(major, minor, build), version);
    }

    private static string ReleaseJson(string tag, bool draft = false, bool prerelease = false, bool includeDigest = true, bool includeSha256Asset = true)
    {
        string hex64 = new string('a', 64);
        string setupAsset = "{ \"name\": \"OpsisSetup.exe\", \"browser_download_url\": \"https://github.com/giorstergiopoulos-design/Opsis/releases/download/" + tag + "/OpsisSetup.exe\""
            + (includeDigest ? ", \"digest\": \"sha256:" + hex64 + "\"" : "") + " }";
        string sha256Asset = includeSha256Asset
            ? ", { \"name\": \"OpsisSetup.exe.sha256\", \"browser_download_url\": \"https://github.com/giorstergiopoulos-design/Opsis/releases/download/" + tag + "/OpsisSetup.exe.sha256\" }"
            : "";
        return "{ \"tag_name\": \"" + tag + "\", "
            + "\"html_url\": \"https://github.com/giorstergiopoulos-design/Opsis/releases/tag/" + tag + "\", "
            + "\"draft\": " + (draft ? "true" : "false") + ", "
            + "\"prerelease\": " + (prerelease ? "true" : "false") + ", "
            + "\"assets\": [ " + setupAsset + sha256Asset + " ] }";
    }

    [Fact]
    public void Parse_NewerVersion_ReturnsUpdateInfo_WithDigestAndChecksumUrl()
    {
        var info = UpdateCheckService.Parse(ReleaseJson("v9.9.9"), new Version(1, 9, 12));
        Assert.NotNull(info);
        Assert.Equal(new Version(9, 9, 9), info!.Version);
        Assert.Equal("https://github.com/giorstergiopoulos-design/Opsis/releases/download/v9.9.9/OpsisSetup.exe", info.SetupUrl);
        Assert.NotNull(info.ChecksumUrl);
        Assert.NotNull(info.Sha256);
        Assert.True(info.CanInstallAutomatically);
    }

    [Fact]
    public void Parse_SameOrOlderVersion_ReturnsNull()
    {
        Assert.Null(UpdateCheckService.Parse(ReleaseJson("v1.9.12"), new Version(1, 9, 12)));
        Assert.Null(UpdateCheckService.Parse(ReleaseJson("v1.0.0"), new Version(1, 9, 12)));
    }

    [Fact]
    public void Parse_Draft_IsIgnored() =>
        Assert.Null(UpdateCheckService.Parse(ReleaseJson("v9.9.9", draft: true), new Version(1, 9, 12)));

    [Fact]
    public void Parse_Prerelease_IsIgnored() =>
        Assert.Null(UpdateCheckService.Parse(ReleaseJson("v9.9.9", prerelease: true), new Version(1, 9, 12)));

    [Fact]
    public void Parse_NoDigestAndNoChecksumAsset_CannotInstallAutomatically()
    {
        var info = UpdateCheckService.Parse(ReleaseJson("v9.9.9", includeDigest: false, includeSha256Asset: false), new Version(1, 9, 12));
        Assert.NotNull(info);
        Assert.Null(info!.Sha256);
        Assert.Null(info.ChecksumUrl);
        Assert.False(info.CanInstallAutomatically);
    }

    [Fact]
    public void Parse_NoDigestButChecksumAssetPresent_CanInstallAutomatically()
    {
        var info = UpdateCheckService.Parse(ReleaseJson("v9.9.9", includeDigest: false, includeSha256Asset: true), new Version(1, 9, 12));
        Assert.NotNull(info);
        Assert.Null(info!.Sha256);
        Assert.NotNull(info.ChecksumUrl);
        Assert.True(info.CanInstallAutomatically); // ChecksumUrl alone is enough - resolved at download time
    }
}
