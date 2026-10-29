using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using MotionDesk.Services;
using Xunit;

namespace Opsis.Tests;

public sealed class SecurityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "opsis_sec_" + Guid.NewGuid().ToString("N")[..8]);
    public SecurityTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    // ---- plugin trust (audit 1)
    [Fact]
    public void ProgramFilesIsProtected_UserFoldersAreNot()
    {
        var roots = new[] { @"C:\Program Files", @"C:\Program Files (x86)" };
        Assert.True(PluginTrust.IsUnderProtectedInstall(@"C:\Program Files\Opsis - Desktop Customization\plugins", roots));
        Assert.True(PluginTrust.IsUnderProtectedInstall(@"C:\Program Files (x86)\Opsis\plugins", roots));
        Assert.False(PluginTrust.IsUnderProtectedInstall(@"C:\Users\x\AppData\Local\Programs\Opsis\plugins", roots));
        Assert.False(PluginTrust.IsUnderProtectedInstall(@"C:\Program Files Evil\plugins", roots));                 // prefix trick
        Assert.False(PluginTrust.IsUnderProtectedInstall(@"C:\Program Files\..\Users\x\plugins", roots));          // traversal is resolved first
    }

    [Fact]
    public void FolderHashChangesWhenAnyDllChanges()
    {
        string f = Path.Combine(_dir, "Crawler"); Directory.CreateDirectory(f);
        File.WriteAllBytes(Path.Combine(f, "A.Plugin.dll"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(f, "Dep.dll"), new byte[] { 9 });
        string h1 = PluginTrust.HashFolder(f);
        Assert.Equal(h1, PluginTrust.HashFolder(f));
        File.WriteAllBytes(Path.Combine(f, "Dep.dll"), new byte[] { 9, 9 });
        Assert.NotEqual(h1, PluginTrust.HashFolder(f));
        File.WriteAllBytes(Path.Combine(f, "Evil.dll"), new byte[] { 0 });                    // a dropped extra dll changes the hash too
        Assert.NotEqual(PluginTrust.HashFolder(f), h1);
    }

    [Fact]
    public void ApprovalIsPerContent_NotPerName()
    {
        string store = Path.Combine(_dir, "plugin-trust.json");
        var s = new PluginTrust.Store(store);
        Assert.False(s.IsApproved("Crawler", "AAAA"));
        s.Approve("Crawler", "AAAA");
        Assert.True(new PluginTrust.Store(store).IsApproved("Crawler", "aaaa"));            // persisted, hash compare is case-insensitive
        Assert.False(new PluginTrust.Store(store).IsApproved("Crawler", "BBBB"));           // changed content -> not approved again
        Assert.False(new PluginTrust.Store(store).IsApproved("Other", "AAAA"));
    }

    // ---- updater TOCTOU (audit 2)
    [Fact]
    public void VerifiedSetupIsLockedAgainstChanges()
    {
        string setup = Path.Combine(_dir, "OpsisSetup.exe");
        File.WriteAllBytes(setup, Encoding.ASCII.GetBytes("installer-bytes"));
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(setup))).ToLowerInvariant();
        using (var locked = UpdateInstallerService.OpenVerified(setup, hash))
        {
            Assert.Throws<IOException>(() => File.WriteAllBytes(setup, new byte[] { 0 }));          // swap attempt while "verified"
            Assert.Throws<IOException>(() => File.Delete(setup));
            Assert.Equal(0, locked.Position);
        }
        File.Delete(setup);                                                                         // released afterwards
    }

    [Fact]
    public void SetupWithWrongHashIsRejectedAndReleased()
    {
        string setup = Path.Combine(_dir, "Bad.exe");
        File.WriteAllBytes(setup, new byte[] { 1, 2, 3 });
        Assert.Throws<InvalidDataException>(() => UpdateInstallerService.OpenVerified(setup, new string('0', 64)));
        File.Delete(setup);                                                                         // handle was closed on failure
    }

    // ---- launch validation (audit 3)
    [Theory]
    [InlineData(@"\server\share\x.exe")]
    [InlineData("https://example.com/x.exe")]
    [InlineData(@"\?\C:\Windows\notepad.exe")]
    [InlineData("notepad.exe")]
    [InlineData("")]
    [InlineData(@"C:\Windows\does_not_exist_12345.exe")]
    public void LaunchRejectsNonLocalOrMissing(string path) => Assert.NotNull(SafeLaunch.Reject(path));

    [Fact]
    public void LaunchRejectsScriptsButAllowsPlainFiles()
    {
        foreach (var ext in new[] { ".ps1", ".vbs", ".js", ".hta", ".scr", ".reg" })
        {
            string f = Path.Combine(_dir, "a" + ext); File.WriteAllText(f, "x");
            Assert.NotNull(SafeLaunch.Reject(f));
        }
        string ok = Path.Combine(_dir, "doc.pdf"); File.WriteAllText(ok, "x");
        Assert.Null(SafeLaunch.Reject(ok));
        Assert.NotNull(SafeLaunch.Reject(ok, requireExe: true));
        string exe = Path.Combine(_dir, "tool.exe"); File.WriteAllText(exe, "x");
        Assert.Null(SafeLaunch.Reject(exe, requireExe: true));
        Assert.Null(SafeLaunch.Reject(_dir));                                                       // a folder opens in Explorer
        Assert.NotNull(SafeLaunch.Reject(_dir, requireExe: true));
    }

    // ---- zip-slip and zip bombs (audit 4)
    private string MakeZip(params (string Name, byte[] Data)[] entries)
    {
        string zip = Path.Combine(_dir, Guid.NewGuid().ToString("N")[..6] + ".zip");
        using var fs = File.Create(zip);
        using var a = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (n, d) in entries) { using var s = a.CreateEntry(n).Open(); s.Write(d); }
        return zip;
    }

    [Fact]
    public void ZipExtractsNormalFiles()
    {
        string dest = Path.Combine(_dir, "ok");
        SafeZip.Extract(MakeZip(("a.txt", new byte[] { 1 }), ("sub/b.txt", new byte[] { 2 })), dest);
        Assert.True(File.Exists(Path.Combine(dest, "a.txt")));
        Assert.True(File.Exists(Path.Combine(dest, "sub", "b.txt")));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("..#evil.txt")]
    [InlineData("sub/../../evil.txt")]
    [InlineData("/abs.txt")]
    [InlineData("C:/Windows/evil.txt")]
    public void ZipSlipIsRejected(string entryName)
    {
        entryName = entryName.Replace("#", ((char)92).ToString());           // # stands for a backslash

        string dest = Path.Combine(_dir, "dest" + Guid.NewGuid().ToString("N")[..4]);
        string zip = MakeZip((entryName, new byte[] { 1 }));
        Assert.Throws<InvalidDataException>(() => SafeZip.Extract(zip, dest));
        Assert.False(File.Exists(Path.Combine(_dir, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(dest)!, "evil.txt")));
    }

    [Fact]
    public void ZipBombsAreRejected()
    {
        string dest = Path.Combine(_dir, "bomb");
        string many = MakeZip(Enumerable.Range(0, 30).Select(i => ($"f{i}.txt", new byte[] { 1 })).ToArray());
        Assert.Throws<InvalidDataException>(() => SafeZip.Extract(many, dest, maxEntries: 10));
        string big = MakeZip(("big.bin", new byte[100_000]));
        Assert.Throws<InvalidDataException>(() => SafeZip.Extract(big, dest, maxTotalBytes: 50_000, maxEntryBytes: 50_000));
    }
}
