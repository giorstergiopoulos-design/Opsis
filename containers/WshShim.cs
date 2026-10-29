// Late-bound replacement for the IWshRuntimeLibrary COM interop assembly (tlbimp is not available in the .NET SDK build).
using System;

namespace IWshRuntimeLibrary
{
    public interface IWshShortcut
    {
        string TargetPath { get; set; }
        string Arguments { get; set; }
        string WorkingDirectory { get; set; }
        string IconLocation { get; set; }
        string Description { get; set; }
        string Hotkey { get; set; }
        int WindowStyle { get; set; }
        string FullName { get; }
        void Save();
    }

    public class WshShell
    {
        public object CreateShortcut(string path)
        {
            var t = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell unavailable");
            dynamic shell = Activator.CreateInstance(t)!;
            return new Shortcut(shell.CreateShortcut(path));
        }

        private sealed class Shortcut : IWshShortcut
        {
            private readonly dynamic _s;
            public Shortcut(dynamic s) { _s = s; }
            public string TargetPath { get => _s.TargetPath; set => _s.TargetPath = value; }
            public string Arguments { get => _s.Arguments; set => _s.Arguments = value; }
            public string WorkingDirectory { get => _s.WorkingDirectory; set => _s.WorkingDirectory = value; }
            public string IconLocation { get => _s.IconLocation; set => _s.IconLocation = value; }
            public string Description { get => _s.Description; set => _s.Description = value; }
            public string Hotkey { get => _s.Hotkey; set => _s.Hotkey = value; }
            public int WindowStyle { get => _s.WindowStyle; set => _s.WindowStyle = value; }
            public string FullName => _s.FullName;
            public void Save() => _s.Save();
        }
    }
}
