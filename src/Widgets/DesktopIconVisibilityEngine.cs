using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MotionDesk.Widgets
{
    // Fences-style: διπλό-κλικ σε κενό σημείο της επιφάνειας εργασίας κρύβει όλα τα εικονίδια
    // εκτός από μια λίστα εξαιρέσεων (Ο Υπολογιστής μου / φάκελος χρήστη / Πίνακας Ελέγχου / Κάδος
    // Ανακύκλωσης) — ζητήθηκε ρητά. Windows δεν εκθέτει καμία επίσημη API για "απόκρυψη
    // συγκεκριμένου εικονιδίου" (μόνο global on/off για ΟΛΑ μαζί) — η μόνη γνωστή τεχνική, ίδια με
    // αυτή που χρησιμοποιεί το ίδιο το Stardock Fences και κάθε clone του, είναι να μετακινηθεί
    // κάθε μη-εξαιρούμενο item του SysListView32 της επιφάνειας εργασίας εκτός ορατής περιοχής
    // (LVM_SETITEMPOSITION) — αυτό ΔΕΝ διαγράφει/μετονομάζει τίποτα, είναι πλήρως αναστρέψιμο
    // (αποθηκεύουμε τις αρχικές θέσεις και τις επαναφέρουμε στο επόμενο διπλό-κλικ).
    //
    // Επειδή το SysListView32 της επιφάνειας εργασίας ανήκει στη διεργασία explorer.exe (άλλη
    // διεργασία), η ανάγνωση κειμένου/θέσης ανά item απαιτεί κλασική cross-process τεχνική
    // (VirtualAllocEx/WriteProcessMemory/ReadProcessMemory/VirtualFreeEx στη διεργασία explorer.exe)
    // — ακριβώς η ίδια τεχνική που χρησιμοποιεί κάθε υπάρχον εργαλείο αυτής της κατηγορίας. Το
    // LVM_SETITEMPOSITION δεν χρειάζεται δείκτη (packed lParam), οπότε είναι απλό SendMessage.
    public sealed class DesktopIconVisibilityEngine
    {
        private static DesktopIconVisibilityEngine? _instance;
        public static DesktopIconVisibilityEngine Instance => _instance ??= new DesktopIconVisibilityEngine();

        // Μένουν ορατά (με επιλογή του χρήστη, βλ. AppSettings.QuickHideKeep*): Ο Υπολογιστής μου / Αυτός ο υπολογιστής, ο φάκελος του
        // χρήστη, ο Πίνακας Ελέγχου και ο Κάδος Ανακύκλωσης — + όποια άλλα εικονίδια διαλέξει ο χρήστης.
        // ΔΙΟΡΘΩΣΗ: τα ονόματα ΔΕΝ μπορούν να είναι hardcoded — στα ελληνικά Windows 11 το εικονίδιο λέγεται «Αυτός ο υπολογιστής»
        // (δεν ήταν στη λίστα), και ο φάκελος χρήστη δείχνει το ΠΛΗΡΕΣ όνομα (π.χ. «Giorgos Stergiopoulos») και όχι το login
        // (Environment.UserName), οπότε έκρυβε το εικονίδιο του χρήστη. Τα ονόματα ζητούνται από το ίδιο το κέλυφος των Windows
        // (display name των ειδικών φακέλων, στη γλώσσα του συστήματος) + στατικά ονόματα εφεδρικά.
        internal static readonly Guid ThisPcId = new("0AC0837C-BBF8-452A-850D-79D08E667CA7");
        internal static readonly Guid ProfileId = new("5E6C858F-0E22-4760-9AFE-EA3317B67173");
        internal static readonly Guid ControlPanelId = new("82A74AEB-AEB4-465C-A014-D097EE346D63");
        internal static readonly Guid RecycleBinId = new("B7534046-3ECB-4C18-BE4E-64CD4CB7D6AC");

        internal static readonly string[] ThisPcFallback = { "This PC", "Αυτός ο υπολογιστής", "Ο Υπολογιστής μου", "Υπολογιστής μου", "Υπολογιστής", "My Computer", "Computer" };
        internal static readonly string[] ControlPanelFallback = { "Control Panel", "Πίνακας Ελέγχου" };
        internal static readonly string[] RecycleBinFallback = { "Recycle Bin", "Κάδος Ανακύκλωσης" };

        /// <summary>Τα ονόματα που μένουν ορατά ΤΩΡΑ, σύμφωνα με τις ρυθμίσεις του χρήστη. Υπολογίζεται μία φορά ανά απόκρυψη/επαναφορά.</summary>
        internal static HashSet<string> BuildWhitelist(Services.AppSettings? settings = null)
        {
            var a = settings ?? Services.AppSettings.Load();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Keep(bool on, Guid id, params string[] fallback)
            {
                if (!on) return;
                var name = ShellDisplayName(id);
                if (!string.IsNullOrWhiteSpace(name)) set.Add(name);
                foreach (var f in fallback) set.Add(f);
            }
            Keep(a.QuickHideKeepThisPc, ThisPcId, ThisPcFallback);
            Keep(a.QuickHideKeepUserFolder, ProfileId, Environment.UserName);
            if (a.QuickHideKeepUserFolder)
            {
                // Το εικονίδιο "Αρχεία χρήστη" της επιφάνειας εργασίας δείχνει το ΠΛΗΡΕΣ όνομα του λογαριασμού (π.χ. "Giorgos Stergiopoulos"),
                // όχι το όνομα του φακέλου (π.χ. "Log9"): ζητείται από το κέλυφος (CLSID User's Files) και από το GetUserNameEx(NameDisplay).
                var full = ShellDisplayNameByParsing("::{59031a47-3f72-44a7-89c5-5595fe6b30ee}");
                if (!string.IsNullOrWhiteSpace(full)) set.Add(full);
                var display = AccountDisplayName();
                if (!string.IsNullOrWhiteSpace(display)) set.Add(display);
            }
            Keep(a.QuickHideKeepControlPanel, ControlPanelId, ControlPanelFallback);
            Keep(a.QuickHideKeepRecycleBin, RecycleBinId, RecycleBinFallback);
            foreach (var extra in a.QuickHideExtraKept) if (!string.IsNullOrWhiteSpace(extra)) set.Add(extra);
            return set;
        }

        /// <summary>Όνομα εμφάνισης (στη γλώσσα του συστήματος) ενός ειδικού φακέλου — για το παράθυρο επιλογής.</summary>
        internal static string? DisplayNameOf(Guid knownFolder) => ShellDisplayName(knownFolder);

        /// <summary>Τα ονόματα των εικονιδίων που υπάρχουν τώρα στην επιφάνεια εργασίας (για το παράθυρο επιλογής). Όταν είναι κρυμμένα, από το αποθηκευμένο state.</summary>
        public static List<string> ListDesktopItemNames()
        {
            var names = new List<string>();
            try
            {
                if (File.Exists(StateFilePath)) { foreach (var sv in LoadState()) names.Add(sv.Name); return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }
                IntPtr list = FindDesktopListView();
                if (list == IntPtr.Zero) return names;
                GetWindowThreadProcessId(list, out uint pid);
                IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
                if (hProcess == IntPtr.Zero) return names;
                try
                {
                    int count = SendMessage(list, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32();
                    for (int i = 0; i < count; i++) { string n = GetItemTextRemote(hProcess, list, i); if (n.Length > 0) names.Add(n); }
                }
                finally { CloseHandle(hProcess); }
            }
            catch (Exception) { }
            return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string? ShellDisplayName(Guid knownFolder)
        {
            IntPtr pidl = IntPtr.Zero;
            try
            {
                if (SHGetKnownFolderIDList(ref knownFolder, 0, IntPtr.Zero, out pidl) != 0 || pidl == IntPtr.Zero) return null;
                var info = new SHFILEINFOW();
                SHGetFileInfoW(pidl, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFOW>(), 0x000000008 /*SHGFI_PIDL*/ | 0x000000200 /*SHGFI_DISPLAYNAME*/);
                return string.IsNullOrWhiteSpace(info.szDisplayName) ? null : info.szDisplayName;
            }
            catch (Exception) { return null; }
            finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
        }

        private static string? ShellDisplayNameByParsing(string parsingName)
        {
            IntPtr pidl = IntPtr.Zero;
            try
            {
                if (SHParseDisplayName(parsingName, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero) return null;
                var info = new SHFILEINFOW();
                SHGetFileInfoW(pidl, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFOW>(), 0x000000008 | 0x000000200);
                return string.IsNullOrWhiteSpace(info.szDisplayName) ? null : info.szDisplayName;
            }
            catch (Exception) { return null; }
            finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
        }

        private static string? AccountDisplayName()
        {
            try
            {
                var sb = new StringBuilder(256); uint n = (uint)sb.Capacity;
                return GetUserNameEx(3 /*NameDisplay*/, sb, ref n) ? sb.ToString() : null;
            }
            catch (Exception) { return null; }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, IntPtr bind, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
        [DllImport("secur32.dll", CharSet = CharSet.Unicode)] private static extern bool GetUserNameEx(int format, StringBuilder name, ref uint size);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFOW
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }
        [DllImport("shell32.dll")] private static extern int SHGetKnownFolderIDList(ref Guid rfid, uint flags, IntPtr token, out IntPtr ppidl);
        [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfoW(IntPtr pidl, uint attrs, ref SHFILEINFOW info, uint cb, uint flags);

        private IntPtr _hookHandle = IntPtr.Zero;
        private LowLevelMouseProc? _hookProc;
        private uint _lastDownTime;
        private Point _lastDownPos;
        private bool _hidden;

        private static string StateFilePath => Path.Combine(Services.AppPaths.AppDataFolder, "desktopicons_hidden_state.json");

        public void Start()
        {
            if (_hookHandle != IntPtr.Zero) return;
            try
            {
                _hookProc = HookProc;
                using var curModule = System.Diagnostics.Process.GetCurrentProcess().MainModule;
                IntPtr hMod = curModule != null ? GetModuleHandle(curModule.ModuleName) : IntPtr.Zero;
                _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, hMod, 0);
            }
            catch { _hookHandle = IntPtr.Zero; }

            // Αν η εφαρμογή τερματίστηκε/κράσαρε ενώ τα εικονίδια ήταν κρυμμένα (το state file υπάρχει
            // ΜΟΝΟ όσο είναι κρυμμένα), τα εικονίδια έμεναν για πάντα εκτός οθόνης (-10000,-10000).
            // Τα επαναφέρουμε στην εκκίνηση.
            if (File.Exists(StateFilePath)) RestoreIfHidden();
        }

        // Επαναφέρει τα κρυμμένα εικονίδια (αν υπάρχουν) — καλείται στην έξοδο και στην εκκίνηση.
        public void RestoreIfHidden()
        {
            if (!_hidden && !File.Exists(StateFilePath)) return;
            try
            {
                IntPtr list = FindDesktopListView();
                if (list == IntPtr.Zero) return;
                GetWindowThreadProcessId(list, out uint pid);
                IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
                if (hProcess == IntPtr.Zero) return;
                try { RestorePositions(hProcess, list); }
                finally { CloseHandle(hProcess); }
                _hidden = false;
                DeskContainerHostEngine.Instance.SetQuickHidden(false);
            }
            catch { }
        }

        private static IntPtr FindDesktopListView()
        {
            IntPtr defView = IntPtr.Zero;
            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero) defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero)
            {
                IntPtr w = IntPtr.Zero;
                while ((w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
                {
                    defView = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (defView != IntPtr.Zero) break;
                }
            }
            return defView == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        }

        // Επαναφορά ΒΑΣΕΙ ΟΝΟΜΑΤΟΣ (όχι index): αν προστέθηκε/διαγράφηκε αρχείο στην επιφάνεια εργασίας
        // όσο τα εικονίδια ήταν κρυμμένα, τα indexes μετατοπίζονται και η επαναφορά κατά index
        // τοποθετούσε λάθος εικονίδια σε λάθος θέσεις.
        private static void RestorePositions(IntPtr hProcess, IntPtr hwndList)
        {
            var keep = BuildWhitelist();
            var byName = new Dictionary<string, SavedIconPos>(StringComparer.OrdinalIgnoreCase);
            foreach (var sv in LoadState()) byName.TryAdd(sv.Name, sv);

            int count = SendMessage(hwndList, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32();
            for (int i = 0; i < count; i++)
            {
                string name = GetItemTextRemote(hProcess, hwndList, i);
                if (keep.Contains(name) && !byName.ContainsKey(name)) continue;       // ένα εικονίδιο που δεν κρύφτηκε ποτέ
                if (byName.TryGetValue(name, out var sv)) SetItemPositionRemote(hwndList, i, sv.X, sv.Y);
            }
            try { File.Delete(StateFilePath); } catch { }
        }

        public void Stop()
        {
            RestoreIfHidden(); // μην αφήνεις τα εικονίδια του χρήστη εκτός οθόνης όταν κλείνει η εφαρμογή
            if (_hookHandle != IntPtr.Zero) { UnhookWindowsHookEx(_hookHandle); _hookHandle = IntPtr.Zero; }
            _hookProc = null;
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && wParam.ToInt64() == WM_LBUTTONDOWN)
                {
                    var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    uint now = data.time;
                    var pos = new Point(data.pt.X, data.pt.Y);
                    bool isDoubleClick = (now - _lastDownTime) <= GetDoubleClickTime()
                        && Math.Abs(pos.X - _lastDownPos.X) <= GetSystemMetrics(SM_CXDOUBLECLK)
                        && Math.Abs(pos.Y - _lastDownPos.Y) <= GetSystemMetrics(SM_CYDOUBLECLK);
                    _lastDownTime = now;
                    _lastDownPos = pos;

                    if (isDoubleClick)
                    {
                        _lastDownTime = 0; // "καταναλώνει" το ζευγάρι — ένα τρίτο συνεχόμενο κλικ δεν μετράει ξανά ως 2ο μισό
                        HandlePossibleDesktopDoubleClick(pos);
                    }
                }
            }
            catch { /* ένας κακός χειρισμός εδώ δεν πρέπει ΠΟΤΕ να ρίξει το global hook / την εφαρμογή */ }
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private void HandlePossibleDesktopDoubleClick(Point screenPos)
        {
            IntPtr hwnd = WindowFromPoint(screenPos);
            if (hwnd == IntPtr.Zero) return;
            if (GetClassNameOf(hwnd) != "SysListView32") return;

            // Επιβεβαίωση ότι είναι όντως το ListView της επιφάνειας εργασίας (μέσα σε
            // SHELLDLL_DefView) και όχι κάποιο άλλο ListView (π.χ. σε παράθυρο Explorer).
            IntPtr parent = GetParent(hwnd);
            if (GetClassNameOf(parent) != "SHELLDLL_DefView") return;

            var client = screenPos;
            ScreenToClient(hwnd, ref client);
            if (!IsEmptyAreaHit(hwnd, client)) return;

            ToggleDesktopIcons(hwnd);
        }

        private static string GetClassNameOf(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return string.Empty;
            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private void ToggleDesktopIcons(IntPtr hwndList)
        {
            int count = SendMessage(hwndList, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32();
            if (count <= 0) return;

            GetWindowThreadProcessId(hwndList, out uint pid);
            IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
            if (hProcess == IntPtr.Zero) return;

            try
            {
                if (!_hidden)
                {
                    var keep = BuildWhitelist();
                    var saved = new List<SavedIconPos>();
                    for (int i = 0; i < count; i++)
                    {
                        string name = GetItemTextRemote(hProcess, hwndList, i);
                        var pos = GetItemPositionRemote(hProcess, hwndList, i);
                        saved.Add(new SavedIconPos { Index = i, Name = name, X = pos.X, Y = pos.Y });
                        if (!keep.Contains(name))
                            SetItemPositionRemote(hwndList, i, -10000, -10000);
                    }
                    SaveState(saved);
                    _hidden = true;
                }
                else
                {
                    RestorePositions(hProcess, hwndList);
                    _hidden = false;
                }

                // Ίδια χειρονομία, ίδια συμπεριφορά με τα πραγματικά Fences: το quick-hide της
                // επιφάνειας εργασίας κρύβει/ξαναδείχνει επίσης όλα τα DeskContainers, εκτός από
                // όσα ο χρήστης έχει σημειώσει ρητά "Exclude from quick-hide".
                DeskContainerHostEngine.Instance.SetQuickHidden(_hidden);
            }
            catch { }
            finally { CloseHandle(hProcess); }
        }

        private static bool IsEmptyAreaHit(IntPtr hwndList, Point clientPoint)
        {
            GetWindowThreadProcessId(hwndList, out uint pid);
            IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
            if (hProcess == IntPtr.Zero) return false;
            IntPtr remoteHit = IntPtr.Zero;
            try
            {
                remoteHit = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)Marshal.SizeOf<LVHITTESTINFO>(), MEM_COMMIT, PAGE_READWRITE);
                if (remoteHit == IntPtr.Zero) return false;
                var hit = new LVHITTESTINFO { pt = new POINT { X = clientPoint.X, Y = clientPoint.Y } };
                if (!WriteProcessMemory(hProcess, remoteHit, ref hit, Marshal.SizeOf<LVHITTESTINFO>(), out _)) return false;
                SendMessage(hwndList, LVM_HITTEST, IntPtr.Zero, remoteHit);
                var buf = new byte[Marshal.SizeOf<LVHITTESTINFO>()];
                if (!ReadProcessMemory(hProcess, remoteHit, buf, buf.Length, out _)) return false;
                int iItem = BitConverter.ToInt32(buf, 12); // pt(8) + flags(4) = offset 12
                return iItem < 0; // κανένα item κάτω από το σημείο -> κενή περιοχή
            }
            catch { return false; }
            finally
            {
                if (remoteHit != IntPtr.Zero) VirtualFreeEx(hProcess, remoteHit, 0, MEM_RELEASE);
                CloseHandle(hProcess);
            }
        }

        private static string GetItemTextRemote(IntPtr hProcess, IntPtr hwndList, int index)
        {
            const int bufChars = 260;
            int bufBytes = bufChars * 2;
            IntPtr remoteText = IntPtr.Zero, remoteItem = IntPtr.Zero;
            try
            {
                remoteText = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)bufBytes, MEM_COMMIT, PAGE_READWRITE);
                remoteItem = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)Marshal.SizeOf<LVITEM>(), MEM_COMMIT, PAGE_READWRITE);
                if (remoteText == IntPtr.Zero || remoteItem == IntPtr.Zero) return string.Empty;

                var item = new LVITEM { mask = LVIF_TEXT, iItem = index, iSubItem = 0, pszText = remoteText, cchTextMax = bufChars };
                if (!WriteProcessMemory(hProcess, remoteItem, ref item, Marshal.SizeOf<LVITEM>(), out _)) return string.Empty;
                SendMessage(hwndList, LVM_GETITEMTEXT, (IntPtr)index, remoteItem);

                var localBuf = new byte[bufBytes];
                if (!ReadProcessMemory(hProcess, remoteText, localBuf, bufBytes, out _)) return string.Empty;
                string text = Encoding.Unicode.GetString(localBuf);
                int nullIdx = text.IndexOf('\0');
                return nullIdx >= 0 ? text[..nullIdx] : text;
            }
            catch { return string.Empty; }
            finally
            {
                if (remoteText != IntPtr.Zero) VirtualFreeEx(hProcess, remoteText, 0, MEM_RELEASE);
                if (remoteItem != IntPtr.Zero) VirtualFreeEx(hProcess, remoteItem, 0, MEM_RELEASE);
            }
        }

        private static Point GetItemPositionRemote(IntPtr hProcess, IntPtr hwndList, int index)
        {
            IntPtr remotePt = IntPtr.Zero;
            try
            {
                remotePt = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)Marshal.SizeOf<POINT>(), MEM_COMMIT, PAGE_READWRITE);
                if (remotePt == IntPtr.Zero) return Point.Empty;
                SendMessage(hwndList, LVM_GETITEMPOSITION, (IntPtr)index, remotePt);
                var buf = new byte[Marshal.SizeOf<POINT>()];
                if (!ReadProcessMemory(hProcess, remotePt, buf, buf.Length, out _)) return Point.Empty;
                return new Point(BitConverter.ToInt32(buf, 0), BitConverter.ToInt32(buf, 4));
            }
            catch { return Point.Empty; }
            finally { if (remotePt != IntPtr.Zero) VirtualFreeEx(hProcess, remotePt, 0, MEM_RELEASE); }
        }

        private static void SetItemPositionRemote(IntPtr hwndList, int index, int x, int y)
        {
            IntPtr lParam = (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF));
            SendMessage(hwndList, LVM_SETITEMPOSITION, (IntPtr)index, lParam);
        }

        private static void SaveState(List<SavedIconPos> saved)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StateFilePath)!);
                MotionDesk.Services.AtomicFile.WriteAllText(StateFilePath, JsonSerializer.Serialize(saved));
            }
            catch (IOException) { }
        }

        private static List<SavedIconPos> LoadState()
        {
            try
            {
                if (File.Exists(StateFilePath))
                    return JsonSerializer.Deserialize<List<SavedIconPos>>(File.ReadAllText(StateFilePath)) ?? new();
            }
            catch (JsonException) { }
            catch (IOException) { }
            return new();
        }

        private sealed class SavedIconPos
        {
            public int Index { get; set; }
            public string Name { get; set; } = "";
            public int X { get; set; }
            public int Y { get; set; }
        }

        // ---- Win32 interop ----

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int SM_CXDOUBLECLK = 36;
        private const int SM_CYDOUBLECLK = 37;

        private const uint LVM_GETITEMCOUNT = 0x1004;      // LVM_FIRST + 4
        private const uint LVM_GETITEMTEXT = 0x1073;       // LVM_GETITEMTEXTW = LVM_FIRST + 115
        private const uint LVM_GETITEMPOSITION = 0x1010;   // LVM_FIRST + 16
        private const uint LVM_SETITEMPOSITION = 0x100F;   // LVM_FIRST + 15
        private const uint LVM_HITTEST = 0x1012;           // LVM_FIRST + 18
        private const uint LVIF_TEXT = 0x0001;

        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;
        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_VM_WRITE = 0x0020;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        private struct LVITEM
        {
            public uint mask;
            public int iItem;
            public int iSubItem;
            public uint state;
            public uint stateMask;
            public IntPtr pszText;
            public int cchTextMax;
            public int iImage;
            public IntPtr lParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LVHITTESTINFO { public POINT pt; public uint flags; public int iItem; public int iSubItem; public int iGroup; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? lpModuleName);
        [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point p);
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hWnd, ref Point lpPoint);
        [DllImport("user32.dll")] private static extern void GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr hObject);
        [DllImport("kernel32.dll")] private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);
        [DllImport("kernel32.dll")] private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);
        [DllImport("kernel32.dll")] private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, ref LVITEM buffer, int nSize, out IntPtr lpNumberOfBytesWritten);
        [DllImport("kernel32.dll")] private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, ref LVHITTESTINFO buffer, int nSize, out IntPtr lpNumberOfBytesWritten);
        [DllImport("kernel32.dll")] private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);
    }
}
