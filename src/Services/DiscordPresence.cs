using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>
/// Optional Discord "Rich Presence" (off by default): shows what Opsis is doing ("Wallpaper: Waves", "Paused") on the user's Discord profile.
/// Talks to the local Discord client over its documented IPC pipe (discord-ipc-0..9); nothing leaves the PC except through Discord itself.
/// Needs an Application ID: create a free application at https://discord.com/developers/applications and paste its ID in Settings.
/// </summary>
public sealed class DiscordPresence : IDisposable
{
    public static DiscordPresence Instance { get; } = new();

    private NamedPipeClientStream? _pipe;
    private string _connectedId = "";
    private System.Windows.Forms.Timer? _timer;
    private readonly long _startedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private string _lastSent = "";

    /// <summary>Applies the current settings: starts, stops or re-targets the presence. Call at startup and after the settings change.</summary>
    public void Apply()
    {
        var s = AppSettings.Load();
        bool on = s.DiscordPresenceEnabled && IsValidId(s.DiscordApplicationId);
        if (!on) { Stop(); return; }
        if (_connectedId != s.DiscordApplicationId) Disconnect();
        if (_timer == null)
        {
            _timer = new System.Windows.Forms.Timer { Interval = 20_000 };      // Discord limits updates to one per 15 s
            _timer.Tick += (_, _) => Push();
            _timer.Start();
        }
        Push();
    }

    public static bool IsValidId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Trim().Length is >= 15 and <= 25 && id.Trim().All(char.IsDigit);

    public void Stop()
    {
        _timer?.Stop(); _timer?.Dispose(); _timer = null;
        try { if (_pipe is { IsConnected: true }) Send(1, "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" + Environment.ProcessId + "},\"nonce\":\"" + Guid.NewGuid() + "\"}"); } catch (Exception) { }
        Disconnect();
        _lastSent = "";
    }

    private int _busy;
    private void Push()
    {
        // the pipe work (connect, handshake, reply) runs on a pool thread: it must never block the UI
        if (System.Threading.Interlocked.Exchange(ref _busy, 1) == 1) return;
        _ = Task.Run(() => { try { PushCore(); } finally { System.Threading.Volatile.Write(ref _busy, 0); } });
    }

    private void PushCore()
    {
        try
        {
            var s = AppSettings.Load();
            string id = s.DiscordApplicationId.Trim();
            if (!EnsureConnected(id)) return;
            var wp = MotionDesk.Widgets.WallpaperSettings.Load();
            var engine = MotionDesk.Widgets.WallpaperHostEngine.Instance;
            string details = engine.IsEnabled ? "Wallpaper: " + wp.Mode : "Desktop customization";
            string state = AppActivity.IsFullscreenAppActive ? "Paused while another app is in front" : "Opsis 2.2.0";
            string key = details + "|" + state;
            if (key == _lastSent) return;
            var payload = new
            {
                cmd = "SET_ACTIVITY",
                args = new { pid = Environment.ProcessId, activity = new { details, state, timestamps = new { start = _startedAt } } },
                nonce = Guid.NewGuid().ToString(),
            };
            Send(1, JsonSerializer.Serialize(payload));
            _lastSent = key;
        }
        catch (Exception) { Disconnect(); }     // Discord closed: try again at the next tick
    }

    private bool EnsureConnected(string id)
    {
        if (_pipe is { IsConnected: true } && _connectedId == id) return true;
        Disconnect();
        for (int i = 0; i < 10; i++)
        {
            NamedPipeClientStream? p = null;
            try
            {
                p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                p.Connect(250);
                _pipe = p;
                Send(0, JsonSerializer.Serialize(new { v = 1, client_id = id }));
                ReadFrame();                    // READY (or an error): read it so the pipe does not fill up
                _connectedId = id;
                return true;
            }
            catch (Exception) { p?.Dispose(); _pipe = null; }
        }
        return false;
    }

    private void Send(int opcode, string json)
    {
        if (_pipe == null) return;
        var body = Encoding.UTF8.GetBytes(json);
        var frame = new byte[8 + body.Length];
        BitConverter.GetBytes(opcode).CopyTo(frame, 0);
        BitConverter.GetBytes(body.Length).CopyTo(frame, 4);
        body.CopyTo(frame, 8);
        _pipe.Write(frame, 0, frame.Length);
        _pipe.Flush();
        if (opcode == 1) _ = Task.Run(() => { try { ReadFrame(); } catch (Exception) { } });      // drain the reply
    }

    private string ReadFrame()
    {
        if (_pipe == null) return "";
        var pipe = _pipe;
        var read = Task.Run(() =>
        {
            var header = new byte[8];
            pipe.ReadExactly(header, 0, 8);
            int len = BitConverter.ToInt32(header, 4);
            if (len <= 0 || len > 1 << 20) return "";
            var body = new byte[len];
            pipe.ReadExactly(body, 0, len);
            return Encoding.UTF8.GetString(body);
        });
        if (!read.Wait(3000)) { Disconnect(); return ""; }      // Discord did not answer: drop the connection, retry later
        return read.Result;
    }

    private void Disconnect()
    {
        try { _pipe?.Dispose(); } catch (Exception) { }
        _pipe = null; _connectedId = "";
    }

    public void Dispose() => Stop();
}
