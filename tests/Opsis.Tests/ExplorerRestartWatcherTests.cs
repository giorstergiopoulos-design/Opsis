using System.Runtime.InteropServices;
using MotionDesk.Widgets;
using Xunit;

namespace Opsis.Tests;

// Bug: after Explorer restarted the screen stayed black. The watcher that listens for the "TaskbarCreated" broadcast was a message-only window, which never receives broadcasts.
public sealed class ExplorerRestartWatcherTests
{
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

    private static void OnSta(Action a)
    {
        Exception? ex = null;
        var t = new Thread(() => { try { a(); } catch (Exception e) { ex = e; } }); t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        if (ex != null) throw ex;
    }

    [Fact]
    public void TheWatcherIsAHiddenTopLevelWindowSoItReceivesBroadcasts()
    {
        OnSta(() =>
        {
            using var w = new ExplorerRestartWatcher();
            bool found = false;
            EnumWindows((h, _) => { if (h == w.Handle) found = true; return true; }, IntPtr.Zero);      // EnumWindows lists top-level windows only, never message-only ones
            Assert.True(found, "the watcher must be a top-level window to receive the TaskbarCreated broadcast");
            Assert.False(IsWindowVisible(w.Handle));
        });
    }

    [Fact]
    public void TheTaskbarCreatedMessageRaisesTheRestartEvent()
    {
        OnSta(() =>
        {
            using var w = new ExplorerRestartWatcher();
            int raised = 0; w.ExplorerRestarted += () => raised++;
            SendMessage(w.Handle, w.TaskbarCreatedMessage, IntPtr.Zero, IntPtr.Zero);          // sent straight to the watcher, never as a machine-wide broadcast
            Assert.Equal(1, raised);
            SendMessage(w.Handle, 0x0001 /* WM_CREATE-like noise */ + 0x400, IntPtr.Zero, IntPtr.Zero);
            Assert.Equal(1, raised);
        });
    }
}
