using System;
using System.Collections.Generic;
using System.Linq;

namespace MotionDesk.Widgets
{
    // Ζητήθηκε ρητά (task tracker t27): "AquaSnap-style neighboring tiles adjust automatically" —
    // χρειάζεται να ξέρουμε ΠΟΙΟ παράθυρο βρίσκεται ΑΥΤΗ ΤΗ ΣΤΙΓΜΗ σε ΠΟΙΑ ζώνη, ανά οθόνη, κάτι
    // που δεν υπήρχε καθόλου πριν (το snapping ήταν εντελώς stateless — βλ. PROJECT_STATE.md).
    //
    // Σκόπιμα ΑΠΛΟ: μόνο in-memory, per-session state (καμία επιμονή σε δίσκο) — "ποιος είναι πού
    // τώρα", όχι μόνιμη ρύθμιση. Ένα stale entry (π.χ. το παράθυρο έκλεισε ή μετακινήθηκε χειροκίνητα
    // αλλού χωρίς νέο snap) απλώς αγνοείται αργότερα από τον caller μέσω IsWindowVisible/GetWindowRect
    // — δεν χρειάζεται ενεργό καθάρισμα εδώ, ο αριθμός ζωνών είναι ούτως ή άλλως μικρός.
    internal static class ZoneOccupancy
    {
        // screen DeviceName -> (zone index -> hwnd)
        private static readonly Dictionary<string, Dictionary<int, IntPtr>> _byScreen = new();

        public static void Set(string screenKey, int zoneIndex, IntPtr hwnd)
        {
            if (string.IsNullOrEmpty(screenKey) || hwnd == IntPtr.Zero) return;
            var map = GetOrCreate(screenKey);
            // Ένα παράθυρο καταλαμβάνει μία μόνο ζώνη τη φορά — καθάρισε προηγούμενες καταχωρήσεις του.
            foreach (var key in map.Where(kv => kv.Value == hwnd).Select(kv => kv.Key).ToList())
                map.Remove(key);
            map[zoneIndex] = hwnd;
        }

        public static IntPtr? Get(string screenKey, int zoneIndex) =>
            _byScreen.TryGetValue(screenKey, out var map) && map.TryGetValue(zoneIndex, out var hwnd) ? hwnd : null;

        // Σε ποια ζώνη (οθόνη + index) είναι καταχωρημένο αυτό το hwnd, αν σε καμία.
        public static (string ScreenKey, int ZoneIndex)? Find(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return null;
            foreach (var (screenKey, map) in _byScreen)
                foreach (var (zoneIndex, h) in map)
                    if (h == hwnd) return (screenKey, zoneIndex);
            return null;
        }

        private static Dictionary<int, IntPtr> GetOrCreate(string screenKey)
        {
            if (!_byScreen.TryGetValue(screenKey, out var map)) { map = new Dictionary<int, IntPtr>(); _byScreen[screenKey] = map; }
            return map;
        }
    }
}
