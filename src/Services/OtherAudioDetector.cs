using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MotionDesk.Services;

/// <summary>
/// Tells whether ANY other application is making sound right now (a game, a browser, a player, a call): the audio sessions of the default
/// output device that are active and not silent, ignoring Opsis itself and its WebView2 processes (that is the wallpaper's own sound).
/// Used to mute the wallpaper audio automatically so it never competes with what the user is actually listening to.
/// </summary>
public sealed class OtherAudioDetector : IDisposable
{
    private MMDeviceEnumerator? _enumerator;
    private const float AudibleThreshold = 0.002f;       // MasterPeakValue 0..1; below this the session is effectively silent

    public bool IsOtherAppPlaying()
    {
        try
        {
            _enumerator ??= new MMDeviceEnumerator();
            using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            device.AudioSessionManager.RefreshSessions();
            HashSet<int>? ours = null;
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                if (s.State != AudioSessionState.AudioSessionStateActive || s.IsSystemSoundsSession) continue;
                if (s.AudioMeterInformation.MasterPeakValue < AudibleThreshold) continue;
                int pid = (int)s.GetProcessID;
                ours ??= MotionDesk.Widgets.ProcessFreezer.TreeOf(Environment.ProcessId).ToHashSet();
                if (!ours.Contains(pid)) return true;
            }
        }
        catch (Exception)
        {
            _enumerator?.Dispose(); _enumerator = null;       // the output device changed: start again next time
        }
        return false;
    }

    public void Dispose() { _enumerator?.Dispose(); _enumerator = null; }
}
