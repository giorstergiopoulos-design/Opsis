using MotionDesk.Services;
using Xunit;

namespace Opsis.Tests;

// The Crawler plugin installs into %AppData%\Opsis\plugins (a user-writable folder), so Opsis must ask for approval. The bug: the question was asked while starting, as a modal box with no
// owner and default "No", and an unanswered / declined plugin was never loaded, so its page was missing. Now start-up never waits: the folder becomes a pending approval.
public sealed class PluginApprovalFlowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "opsis_appr_" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _folder;
    private readonly PluginHost _host = new();
    private readonly List<string> _loaded = new();

    public PluginApprovalFlowTests()
    {
        _folder = Path.Combine(_root, "plugins", "Opsis - Desktop Customization");
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "OpsisNewsCrawler.OpsisPlugin.dll"), "dll v1");
        _host.TrustStoreOverride = new PluginTrust.Store(Path.Combine(_root, "plugin-trust.json"));
        _host.FolderLoader = f => _loaded.Add(f);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    [Fact]
    public void AnUnapprovedUserFolderIsNotLoadedAndBecomesPendingWithoutAnyPrompt()
    {
        Assert.False(_host.ConsiderFolder(_folder, trustedRoot: false));
        Assert.Empty(_loaded);
        var p = Assert.Single(_host.PendingApprovals);
        Assert.Equal("Opsis - Desktop Customization", p.Name);
        Assert.Equal(64, p.Sha256.Length);
    }

    [Fact]
    public void ATrustedRootLoadsWithoutApproval()
    {
        Assert.True(_host.ConsiderFolder(_folder, trustedRoot: true));
        Assert.Single(_loaded);
        Assert.Empty(_host.PendingApprovals);
    }

    [Fact]
    public void ApprovingLoadsThePluginAndClearsThePendingEntry()
    {
        _host.ConsiderFolder(_folder, false);
        int n = _host.ResolvePending(_ => true);
        Assert.Equal(1, n);
        Assert.Single(_loaded);
        Assert.Empty(_host.PendingApprovals);
        // the approval is remembered: the next start loads it straight away
        var next = new PluginHost { TrustStoreOverride = _host.TrustStoreOverride, FolderLoader = f => _loaded.Add(f + "#2") };
        Assert.True(next.ConsiderFolder(_folder, false));
        Assert.Contains(_loaded, x => x.EndsWith("#2"));
    }

    [Fact]
    public void DecliningKeepsItPendingSoItCanBeApprovedLaterFromTheTray()
    {
        _host.ConsiderFolder(_folder, false);
        Assert.Equal(0, _host.ResolvePending(_ => false));
        Assert.Empty(_loaded);
        Assert.Single(_host.PendingApprovals);
        Assert.Equal(1, _host.ResolvePending(_ => true));
        Assert.Single(_loaded);
    }

    [Fact]
    public void AFailingPromptIsTreatedAsNoAndDoesNotThrow()
    {
        _host.ConsiderFolder(_folder, false);
        Assert.Equal(0, _host.ResolvePending(_ => throw new InvalidOperationException("no window")));
        Assert.Single(_host.PendingApprovals);
    }

    [Fact]
    public void ChangedContentNeedsANewApproval()
    {
        _host.ConsiderFolder(_folder, false);
        _host.ResolvePending(_ => true);
        File.WriteAllText(Path.Combine(_folder, "OpsisNewsCrawler.OpsisPlugin.dll"), "dll v2 (updated)");
        var again = new PluginHost { TrustStoreOverride = _host.TrustStoreOverride, FolderLoader = f => _loaded.Add(f + "#3") };
        Assert.False(again.ConsiderFolder(_folder, false));
        Assert.Single(again.PendingApprovals);
    }
}
