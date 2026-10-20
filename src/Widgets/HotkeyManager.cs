using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MotionDesk.Widgets
{
    // Message-only παράθυρο που καταγράφει παγκόσμιες συντομεύσεις πληκτρολογίου (RegisterHotKey)
    // και σηκώνει ένα .NET event, ώστε να μη χρειάζεται να "κρύβουμε" hotkey λογική μέσα σε φόρμες UI.
    public sealed class HotkeyManager : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_SHIFT = 0x0004;

        private int _nextId = 9000;
        private readonly System.Collections.Generic.Dictionary<int, Action> _handlers = new();

        public HotkeyManager()
        {
            CreateHandle(new CreateParams());
        }

        public void RegisterCtrlAlt(char key, Action onPressed)
        {
            int id = _nextId++;
            _handlers[id] = onPressed;
            RegisterHotKey(Handle, id, MOD_CONTROL | MOD_ALT, (uint)key);
        }

        // Υπερφόρτωση για πλήκτρα χωρίς απλή χαρακτήρα-αναπαράσταση (βελάκια κ.λπ.) — το Keys enum
        // έχει ήδη τους σωστούς Win32 virtual-key κωδικούς, απλή μετατροπή.
        public void RegisterCtrlAlt(Keys key, Action onPressed)
        {
            int id = _nextId++;
            _handlers[id] = onPressed;
            RegisterHotKey(Handle, id, MOD_CONTROL | MOD_ALT, (uint)key);
        }

        // ΣΗΜΑΝΤΙΚΟ (βρέθηκε κατά τη δοκιμή του keyboard zone navigation): το απλό Ctrl+Alt+βελάκι
        // είναι ΗΔΗ δεσμευμένο συστημικά σε πολλά μηχανήματα από τον οδηγό γραφικών Intel/NVIDIA/AMD
        // (περιστροφή οθόνης) — το δικό μας RegisterHotKey απέτυχε σιωπηλά (ERROR_HOTKEY_ALREADY_
        // REGISTERED) χωρίς το combo να κάνει ΤΙΠΟΤΑ, επιβεβαιωμένο με αυτοτελή δοκιμή RegisterHotKey
        // σε αυτό το μηχάνημα. Το Ctrl+Alt+Shift+βελάκι είναι πολύ λιγότερο πιθανό να συγκρουστεί.
        public void RegisterCtrlAltShift(Keys key, Action onPressed)
        {
            int id = _nextId++;
            _handlers[id] = onPressed;
            RegisterHotKey(Handle, id, MOD_CONTROL | MOD_ALT | MOD_SHIFT, (uint)key);
        }

        // Χωρίς Alt: χρησιμοποιείται για "μετακίνηση εστίασης" (ZoneSnapEngine.FocusAdjacentZone) —
        // δεν συγκρούεται με το Ctrl+Alt+βελάκι των οδηγών γραφικών (βλ. σχόλιο παραπάνω) αφού δεν
        // περιλαμβάνει καθόλου Alt, ούτε με το Ctrl+Alt+Shift+βελάκι ("μετακίνηση παραθύρου") αφού
        // εδώ δεν πατιέται Alt.
        public void RegisterCtrlShift(Keys key, Action onPressed)
        {
            int id = _nextId++;
            _handlers[id] = onPressed;
            RegisterHotKey(Handle, id, MOD_CONTROL | MOD_SHIFT, (uint)key);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && _handlers.TryGetValue(m.WParam.ToInt32(), out var handler))
            {
                handler.Invoke();
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            foreach (var id in _handlers.Keys)
                UnregisterHotKey(Handle, id);
            _handlers.Clear();
            DestroyHandle();
        }

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
