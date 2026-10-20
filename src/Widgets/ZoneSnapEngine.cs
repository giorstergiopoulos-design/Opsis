using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MotionDesk.Widgets
{
    // Πραγματική συμπεριφορά FancyZones (PowerToys), όχι απλό "άσε το οπουδήποτε μέσα σε ζώνη":
    // κρατάς Shift ΕΝΩ σέρνεις ένα κανονικό παράθυρο -> εμφανίζεται ημιδιάφανο overlay με τις
    // ζώνες της οθόνης κάτω από τον δείκτη, η ζώνη κάτω από τον δείκτη τονίζεται, και το άφημα
    // (mouse up) ΜΕΣΑ σε highlighted ζώνη ενώ κρατάς ΑΚΟΜΑ Shift την "κουμπώνει".
    //
    // ΝΕΟ (v1.5.0 follow-up, στα πρότυπα του AquaSnap): ΧΩΡΙΣ Shift, ένα ελαφρύ "μαγνητικό" snap
    // ενεργοποιείται ΜΟΝΟ όταν ο δείκτης πλησιάζει μία από τις 4 άκρες/4 γωνίες της οθόνης —
    // δείχνει προεπισκόπηση μισού/τετάρτου της οθόνης, ανεξάρτητα από το προσαρμοσμένο DeskZones
    // layout του χρήστη. Ρητή απόφαση: το μαγνητικό snap είναι πάντα ενεργό (καμία πύλη Shift),
    // αλλά ΜΟΝΟ κοντά σε άκρες — μακριά από άκρες, το σύρσιμο παραμένει απολύτως κανονικό, καμία
    // παρέμβαση. Το πλήρες πλέγμα ζωνών (DeskZones) συνεχίζει να χρειάζεται Shift όπως πριν.
    //
    // ΔΙΟΡΘΩΣΗ πραγματικού bug (v1.5.0 follow-up — ο χρήστης ανέφερε "δεν λειτουργεί" στο πραγματικό
    // Shift+drag): η προηγούμενη υλοποίηση ανίχνευε "άρχισε drag" μόνωνικά με ένα raw low-level
    // mouse hook (WH_MOUSE_LL) + το δικό της heuristic "κουμπί κάτω πάνω σε ένα ορατό/WS_CAPTION
    // παράθυρο, μετά κινήθηκε >6px". Αυτό το heuristic δεν είναι το ίδιο σήμα που χρησιμοποιούν τα
    // ίδια τα Windows για να ξέρουν πότε ΠΡΑΓΜΑΤΙΚΑ ξεκίνησε ένα move-operation — αναξιόπιστο σε
    // πραγματική χρήση (ακριβώς αυτό ανέφερε ο χρήστης) ΚΑΙ σε αυτοματοποιημένο testing (ένα
    // synthetic drag μετακινούσε πραγματικά το παράθυρο, αλλά δεν ενεργοποιούσε ποτέ το hook).
    //
    // Αντί γι' αυτό, χρησιμοποιούμε το ΔΗΜΟΣΙΟ, τεκμηριωμένο accessibility API των ίδιων των
    // Windows (SetWinEventHook + EVENT_SYSTEM_MOVESIZESTART/END, Microsoft Learn) — το ΙΔΙΟ
    // authoritative σήμα που στέλνουν τα ίδια τα Windows σε ΚΑΘΕ access­ibility tool (screen
    // readers, automation frameworks, και ναι, και στο πραγματικό FancyZones) τη στιγμή που ένα
    // παράθυρο ΠΡΑΓΜΑΤΙΚΑ μπαίνει/βγαίνει από το native move-ή-resize loop των Windows. Αυτό δεν
    // είναι αντιγραφή κώδικα κάποιου συγκεκριμένου εργαλείου — είναι η τεκμηριωμένη, δημόσια σωστή
    // χρήση ενός OS API που προορίζεται ακριβώς για αυτή τη δουλειά. Η υλοποίηση από κάτω
    // (ονόματα, δομή, σχόλια) είναι δική μας, γραμμένη από την αρχή.
    public static class ZoneSnapEngine
    {
        private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
        private const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        // ΠΡΙΝ χρησιμοποιούσαμε WINEVENT_SKIPOWNPROCESS και έτσι το ίδιο το παράθυρο του Opsis δεν κούμπωνε ποτέ σε ζώνη
        // (ενώ τα παράθυρα όλων των άλλων εφαρμογών κούμπωναν). Τώρα τα events της δικής μας διεργασίας φτάνουν εδώ και τα
        // φιλτράρει το IsCandidateWindow: επιτρέπεται ΜΟΝΟ το κύριο παράθυρο (AllowOwnWindow), όχι widgets/containers/overlays.
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int VK_SHIFT = 0x10;

        // Πλάτος (σε px) της "μαγνητικής" ζώνης γύρω από κάθε άκρη/γωνία της οθόνης — αρκετά
        // μεγάλο για να είναι εύκολο να το πετύχεις με το ποντίκι, αρκετά μικρό για να μην
        // ενεργοποιείται κατά λάθος ενώ σέρνεις κάπου στη μέση της οθόνης.
        private const int MagneticThresholdPx = 24;

        private enum MagneticRegion { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

        private static IntPtr _hookId = IntPtr.Zero;
        private static WinEventProc? _callback;
        private static System.Windows.Forms.Timer? _pollTimer;
        private static IntPtr _draggedWindow = IntPtr.Zero;
        // Θέση/μέγεθος του παραθύρου ΠΡΙΝ αρχίσει το move/resize — χρειάζεται για να ξέρουμε ΜΕΤΑ αν
        // ήταν αλλαγή μεγέθους (όχι απλή μετακίνηση) και ΠΟΙΑ άκρη κινήθηκε, για το neighbor-resize.
        private static Rectangle _dragStartRect;
        private static ZoneOverlayWindow? _overlay;
        private static Screen? _overlayScreen;
        private static bool _overlayIsMagnetic;
        private static MagneticRegion _currentMagneticRegion = MagneticRegion.None;

        public static void Start()
        {
            if (_hookId != IntPtr.Zero) return;
            _callback = OnWinEvent;
            // WINEVENT_OUTOFCONTEXT: δουλεύει μέσα σε managed (.NET) κώδικα χωρίς να χρειάζεται
            // native DLL injection στις διεργασίες-στόχους — απλά απαιτεί το thread που κάλεσε
            // SetWinEventHook να "τρέχει" ένα κανονικό message loop (το κύριο UI thread του
            // Application.Run το κάνει ήδη). Το εύρος START..END καλύπτει και τα δύο events με μία
            // εγγραφή hook, αφού είναι διαδοχικές σταθερές (0x000A, 0x000B).
            _hookId = SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND,
                IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
        }

        public static void Stop()
        {
            if (_hookId == IntPtr.Zero) return;
            UnhookWinEvent(_hookId);
            _hookId = IntPtr.Zero;
            _callback = null;
            StopTracking();
        }

        private static bool IsShiftDown() => (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

        private static void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
        {
            try
            {
                // idObject/idChild != 0 σημαίνει κάποιο εσωτερικό UI στοιχείο (κουμπί, scrollbar…)
                // που μπήκε σε move/size, όχι ολόκληρο παράθυρο — αγνοείται.
                if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return;

                if (eventType == EVENT_SYSTEM_MOVESIZESTART)
                {
                    if (!IsCandidateWindow(hwnd)) return;
                    _draggedWindow = hwnd;
                    _dragStartRect = GetWindowRect(hwnd, out var startR) ? ToRectangle(startR) : Rectangle.Empty;
                    StartTracking();
                }
                else if (eventType == EVENT_SYSTEM_MOVESIZEEND && hwnd == _draggedWindow)
                {
                    CommitOrCancel();
                }
            }
            catch { /* ένα hook callback δεν πρέπει ΠΟΤΕ να πετάξει εξαίρεση προς τα έξω */ }
        }

        // Όσο διαρκεί το drag, δεν έχουμε ξεχωριστό "location changed" event να ακούσουμε (θα
        // απαιτούσε μια ΔΕΥΤΕΡΗ, πιο θορυβώδη εγγραφή hook) — απλό polling της θέσης του δείκτη σε
        // ρυθμό ~60Hz αρκεί για ένα responsive overlay χωρίς αισθητή καθυστέρηση.
        private static void StartTracking()
        {
            if (_pollTimer == null)
            {
                _pollTimer = new System.Windows.Forms.Timer { Interval = 16 };
                _pollTimer.Tick += (_, _) => OnPollTick();
            }
            _pollTimer.Start();
        }

        private static void StopTracking()
        {
            _pollTimer?.Stop();
            HideOverlay();
            _draggedWindow = IntPtr.Zero;
        }

        private static void OnPollTick()
        {
            if (_draggedWindow == IntPtr.Zero || !IsWindowVisible(_draggedWindow))
            {
                StopTracking();
                return;
            }
            GetCursorPos(out var pt);
            var screenPt = new Point(pt.X, pt.Y);
            if (IsShiftDown()) UpdateZoneGridOverlay(screenPt);
            else UpdateMagneticOverlay(screenPt);
        }

        private static void CommitOrCancel()
        {
            _pollTimer?.Stop();
            try
            {
                // Το πλέγμα ζωνών (Shift) κουμπώνει μόνο αν το Shift είναι ΑΚΟΜΑ πατημένο στο
                // άφημα (όπως πριν)· το μαγνητικό snap (χωρίς Shift) κουμπώνει όποτε δείχνει
                // προεπισκόπηση, χωρίς όρο για το Shift — αυτό είναι το "πάντα ενεργό" κομμάτι.
                bool shouldCommit = _overlayIsMagnetic || IsShiftDown();
                if (shouldCommit && _overlay != null)
                {
                    var zoneBounds = _overlay.GetHighlightedZoneBounds();
                    if (zoneBounds.HasValue && _draggedWindow != IntPtr.Zero)
                    {
                        var r = AdjustForInvisibleFrame(_draggedWindow, zoneBounds.Value);
                        SetWindowPos(_draggedWindow, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE);
                        // Μόνο για πραγματικό πλέγμα DeskZones (όχι το ad-hoc μαγνητικό μισό/τέταρτο) —
                        // έχει σταθερό, αποθηκευμένο index νόημα για το neighbor-resize παρακάτω.
                        if (!_overlayIsMagnetic && _overlayScreen != null)
                            ZoneOccupancy.Set(_overlayScreen.DeviceName, _overlay.HighlightedIndex, _draggedWindow);
                    }
                }
                else
                {
                    // Δεν κουμπώνει σε ΝΕΑ ζώνη — αν ήταν ήδη καταχωρημένο σε μία (από προηγούμενο
                    // snap) και άλλαξε μέγεθος (όχι απλή μετακίνηση), σπρώξε το γειτονικό παράθυρο
                    // να γεμίσει/αδειάσει μαζί του, AquaSnap-style.
                    TryResizeNeighbors(_draggedWindow, _dragStartRect);
                }
            }
            finally
            {
                HideOverlay();
                _draggedWindow = IntPtr.Zero;
            }
        }

        // ΝΕΟ (task tracker t27, "neighbor-tile auto-resize"): όταν ένα ήδη-κουμπωμένο παράθυρο
        // αλλάζει μέγεθος σέρνοντας την άκρη του (όχι ξανά-snap σε άλλη ζώνη), το γειτονικό παράθυρο
        // που μοιράζεται ΑΚΡΙΒΩΣ αυτή την άκρη προσαρμόζεται μαζί του — καμία αλλαγή στο ΑΠΟΘΗΚΕΥΜΕΝΟ
        // DeskZones layout (ZoneLayoutStore), μόνο στα ήδη-ανοιχτά παράθυρα, αυτή τη φορά.
        private const int ResizeNoisePx = 3;
        private const int AdjacencyTolerancePx = 4;
        private const int MinNeighborSizePx = 120;

        private enum Edge { Left, Right, Top, Bottom }

        private static void TryResizeNeighbors(IntPtr hwnd, Rectangle startRect)
        {
            if (hwnd == IntPtr.Zero || startRect.IsEmpty) return;
            if (!GetWindowRect(hwnd, out var endR)) return;
            var endRect = ToRectangle(endR);

            bool widthChanged = Math.Abs(endRect.Width - startRect.Width) > ResizeNoisePx;
            bool heightChanged = Math.Abs(endRect.Height - startRect.Height) > ResizeNoisePx;
            if (!widthChanged && !heightChanged) return; // απλή μετακίνηση, όχι αλλαγή μεγέθους

            var found = ZoneOccupancy.Find(hwnd);
            if (found == null) return;
            var (screenKey, zoneIndex) = found.Value;
            var screen = Array.Find(Screen.AllScreens, s => s.DeviceName == screenKey);
            if (screen == null) return;
            var layout = ZoneLayoutStore.GetLayout(screenKey);
            if (zoneIndex < 0 || zoneIndex >= layout.Zones.Count) return;

            int dLeft = endRect.Left - startRect.Left;
            int dRight = endRect.Right - startRect.Right;
            int dTop = endRect.Top - startRect.Top;
            int dBottom = endRect.Bottom - startRect.Bottom;

            if (Math.Abs(dRight) > ResizeNoisePx && Math.Abs(dLeft) <= ResizeNoisePx)
                ResizeNeighborSharingEdge(layout, screenKey, screen.WorkingArea, zoneIndex, Edge.Right, endRect.Right);
            if (Math.Abs(dLeft) > ResizeNoisePx && Math.Abs(dRight) <= ResizeNoisePx)
                ResizeNeighborSharingEdge(layout, screenKey, screen.WorkingArea, zoneIndex, Edge.Left, endRect.Left);
            if (Math.Abs(dBottom) > ResizeNoisePx && Math.Abs(dTop) <= ResizeNoisePx)
                ResizeNeighborSharingEdge(layout, screenKey, screen.WorkingArea, zoneIndex, Edge.Bottom, endRect.Bottom);
            if (Math.Abs(dTop) > ResizeNoisePx && Math.Abs(dBottom) <= ResizeNoisePx)
                ResizeNeighborSharingEdge(layout, screenKey, screen.WorkingArea, zoneIndex, Edge.Top, endRect.Top);
        }

        private static void ResizeNeighborSharingEdge(ZoneLayoutData layout, string screenKey, Rectangle workingArea, int movedZoneIndex, Edge edge, int newAbsoluteEdgePx)
        {
            // Η τοπολογία (ποιος είναι "γείτονας" σε ποια άκρη) βγαίνει από το ΣΤΑΘΕΡΟ αποθηκευμένο
            // πρότυπο ζωνών — όχι από τρέχουσες θέσεις παραθύρων, που μπορεί να έχουν ήδη
            // μετατοπιστεί από προηγούμενο drag.
            var movedTemplateAbs = layout.Zones[movedZoneIndex].ToAbsolute(workingArea);

            for (int i = 0; i < layout.Zones.Count; i++)
            {
                if (i == movedZoneIndex) continue;
                var candTemplateAbs = layout.Zones[i].ToAbsolute(workingArea);
                bool shares = edge switch
                {
                    Edge.Right => Math.Abs(candTemplateAbs.Left - movedTemplateAbs.Right) <= AdjacencyTolerancePx && VerticalOverlap(candTemplateAbs, movedTemplateAbs),
                    Edge.Left => Math.Abs(candTemplateAbs.Right - movedTemplateAbs.Left) <= AdjacencyTolerancePx && VerticalOverlap(candTemplateAbs, movedTemplateAbs),
                    Edge.Bottom => Math.Abs(candTemplateAbs.Top - movedTemplateAbs.Bottom) <= AdjacencyTolerancePx && HorizontalOverlap(candTemplateAbs, movedTemplateAbs),
                    Edge.Top => Math.Abs(candTemplateAbs.Bottom - movedTemplateAbs.Top) <= AdjacencyTolerancePx && HorizontalOverlap(candTemplateAbs, movedTemplateAbs),
                    _ => false
                };
                if (!shares) continue;

                var occupant = ZoneOccupancy.Get(screenKey, i);
                if (occupant == null || !IsWindowVisible(occupant.Value) || !GetWindowRect(occupant.Value, out var curR)) continue;
                var cur = ToRectangle(curR);

                // Τέντωσε/μάζεψε τον γείτονα ΜΟΝΟ στην άκρη που συνορεύει με την κινούμενη, κρατώντας
                // τις άλλες τρεις άκρες όπως είναι ΤΩΡΑ (όχι το αρχικό πρότυπο) — σωστό ακόμα κι αν ο
                // γείτονας είχε ήδη αλλάξει χέρι-χέρι πριν.
                Rectangle updated = edge switch
                {
                    Edge.Right => Rectangle.FromLTRB(newAbsoluteEdgePx, cur.Top, cur.Right, cur.Bottom),
                    Edge.Left => Rectangle.FromLTRB(cur.Left, cur.Top, newAbsoluteEdgePx, cur.Bottom),
                    Edge.Bottom => Rectangle.FromLTRB(cur.Left, newAbsoluteEdgePx, cur.Right, cur.Bottom),
                    Edge.Top => Rectangle.FromLTRB(cur.Left, cur.Top, cur.Right, newAbsoluteEdgePx),
                    _ => cur
                };
                if (updated.Width < MinNeighborSizePx || updated.Height < MinNeighborSizePx) continue; // ασφάλεια: μην συρρικνώσεις υπερβολικά

                var adjusted = AdjustForInvisibleFrame(occupant.Value, updated);
                SetWindowPos(occupant.Value, IntPtr.Zero, adjusted.X, adjusted.Y, adjusted.Width, adjusted.Height, SWP_NOZORDER | SWP_NOACTIVATE);
            }
        }

        private static bool VerticalOverlap(Rectangle a, Rectangle b) => a.Top < b.Bottom - AdjacencyTolerancePx && b.Top < a.Bottom - AdjacencyTolerancePx;
        private static bool HorizontalOverlap(Rectangle a, Rectangle b) => a.Left < b.Right - AdjacencyTolerancePx && b.Left < a.Right - AdjacencyTolerancePx;

        private static Rectangle ToRectangle(RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

        // BUG FIX (v1.5.0 request): παράθυρα δεν "τέντωναν" μέχρι τα πραγματικά περιθώρια της
        // οθόνης όταν κουμπώνονταν σε ζώνη ακουμπισμένη στην άκρη — τα περισσότερα παράθυρα των
        // Windows 10/11 έχουν ένα αόρατο περιθώριο αλλαγής μεγέθους γύρω τους (μέρος του drop-
        // shadow), οπότε το GetWindowRect/SetWindowPos "βλέπει" ένα πλαίσιο λίγα pixels
        // μεγαλύτερο από το πραγματικά ΟΡΑΤΟ περιεχόμενο. Χωρίς αντιστάθμιση, ένα SetWindowPos
        // στα ακριβή όρια της ζώνης άφηνε ένα κενό ανάμεσα στο ορατό παράθυρο και την άκρη της
        // οθόνης. Η σύγκριση GetWindowRect έναντι DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)
        // δίνει ακριβώς αυτό το αόρατο περιθώριο ανά πλευρά· το αντισταθμίζουμε πριν το SetWindowPos
        // ώστε το ΟΡΑΤΟ πλαίσιο (όχι το "τεχνικό" WindowRect) να ταυτίζεται με τη ζώνη. (Αυτό ήταν
        // ήδη σωστό πριν — το πραγματικό δεύτερο bug ήταν στην ίδια τη γεωμετρία των ζωνών, βλ.
        // ZoneLayoutStore.BuildTemplate.)
        private static Rectangle AdjustForInvisibleFrame(IntPtr hwnd, Rectangle zoneRect)
        {
            try
            {
                if (!GetWindowRect(hwnd, out var windowRect)) return zoneRect;
                int hr = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT visibleRect, Marshal.SizeOf<RECT>());
                if (hr != 0) return zoneRect;

                int leftMargin = visibleRect.Left - windowRect.Left;
                int topMargin = visibleRect.Top - windowRect.Top;
                int rightMargin = windowRect.Right - visibleRect.Right;
                int bottomMargin = windowRect.Bottom - visibleRect.Bottom;

                return new Rectangle(
                    zoneRect.X - leftMargin,
                    zoneRect.Y - topMargin,
                    zoneRect.Width + leftMargin + rightMargin,
                    zoneRect.Height + topMargin + bottomMargin);
            }
            catch { return zoneRect; }
        }

        private static void UpdateZoneGridOverlay(Point pt)
        {
            var screen = Screen.FromPoint(pt);
            var layout = ZoneLayoutStore.GetLayout(screen.DeviceName);
            if (layout.Zones.Count == 0) { HideOverlay(); return; }

            if (_overlay == null || _overlay.IsDisposed) _overlay = new ZoneOverlayWindow();
            if (_overlayScreen?.DeviceName != screen.DeviceName || _overlayIsMagnetic)
            {
                _overlay.ShowForScreen(screen, layout);
                _overlayScreen = screen;
                _overlayIsMagnetic = false;
            }
            _overlay.UpdateHighlight(_overlay.FindZoneIndexAt(pt));
        }

        // AquaSnap-style μαγνητικό snap: ανεξάρτητο από το αποθηκευμένο DeskZones layout του
        // χρήστη — υπολογίζει απευθείας μισό/τέταρτο της οθόνης ανάλογα με το ποια άκρη/γωνία
        // πλησιάζει ο δείκτης, και δεν εμφανίζει τίποτα μακριά από άκρες.
        private static void UpdateMagneticOverlay(Point pt)
        {
            var screen = Screen.FromPoint(pt);
            var region = DetectMagneticRegion(pt, screen);
            if (region == MagneticRegion.None) { HideOverlay(); return; }

            if (_overlay == null || _overlay.IsDisposed) _overlay = new ZoneOverlayWindow();
            if (_overlayScreen?.DeviceName != screen.DeviceName || !_overlayIsMagnetic || region != _currentMagneticRegion)
            {
                var layout = new ZoneLayoutData { Template = "Magnetic", Zones = { RegionToZoneRect(region) } };
                _overlay.ShowForScreen(screen, layout);
                _overlay.UpdateHighlight(0);
                _overlayScreen = screen;
                _overlayIsMagnetic = true;
                _currentMagneticRegion = region;
            }
        }

        // Άκρη που συνορεύει με ΑΛΛΗ οθόνη δεν είναι πραγματική άκρη: σέρνοντας ένα παράθυρο από τη μία
        // οθόνη στην άλλη ο δείκτης περνά από εκεί, και το μαγνητικό snap ενεργοποιούνταν (ή και
        // κούμπωνε αν το άφηνες κοντά) ενώ ο χρήστης απλώς άλλαζε οθόνη.
        private static bool HasNeighborScreen(Rectangle bounds, int dx, int dy, Point pt)
        {
            var probe = new Point(
                dx < 0 ? bounds.Left - 2 : dx > 0 ? bounds.Right + 1 : pt.X,
                dy < 0 ? bounds.Top - 2 : dy > 0 ? bounds.Bottom + 1 : pt.Y);
            return Screen.AllScreens.Any(sc => sc.Bounds.Contains(probe));
        }

        private static MagneticRegion DetectMagneticRegion(Point screenPt, Screen screen)
        {
            var workingArea = screen.WorkingArea;
            int localX = screenPt.X - workingArea.X;
            int localY = screenPt.Y - workingArea.Y;
            var b = screen.Bounds;
            bool nearLeft = localX <= MagneticThresholdPx && !HasNeighborScreen(b, -1, 0, screenPt);
            bool nearRight = localX >= workingArea.Width - MagneticThresholdPx && !HasNeighborScreen(b, 1, 0, screenPt);
            bool nearTop = localY <= MagneticThresholdPx && !HasNeighborScreen(b, 0, -1, screenPt);
            bool nearBottom = localY >= workingArea.Height - MagneticThresholdPx && !HasNeighborScreen(b, 0, 1, screenPt);

            if (nearLeft && nearTop) return MagneticRegion.TopLeft;
            if (nearRight && nearTop) return MagneticRegion.TopRight;
            if (nearLeft && nearBottom) return MagneticRegion.BottomLeft;
            if (nearRight && nearBottom) return MagneticRegion.BottomRight;
            if (nearLeft) return MagneticRegion.Left;
            if (nearRight) return MagneticRegion.Right;
            if (nearTop) return MagneticRegion.Top;
            if (nearBottom) return MagneticRegion.Bottom;
            return MagneticRegion.None;
        }

        private static ZoneRect RegionToZoneRect(MagneticRegion region) => region switch
        {
            MagneticRegion.Left => new ZoneRect { X = 0, Y = 0, Width = 0.5, Height = 1 },
            MagneticRegion.Right => new ZoneRect { X = 0.5, Y = 0, Width = 0.5, Height = 1 },
            MagneticRegion.Top => new ZoneRect { X = 0, Y = 0, Width = 1, Height = 0.5 },
            MagneticRegion.Bottom => new ZoneRect { X = 0, Y = 0.5, Width = 1, Height = 0.5 },
            MagneticRegion.TopLeft => new ZoneRect { X = 0, Y = 0, Width = 0.5, Height = 0.5 },
            MagneticRegion.TopRight => new ZoneRect { X = 0.5, Y = 0, Width = 0.5, Height = 0.5 },
            MagneticRegion.BottomLeft => new ZoneRect { X = 0, Y = 0.5, Width = 0.5, Height = 0.5 },
            MagneticRegion.BottomRight => new ZoneRect { X = 0.5, Y = 0.5, Width = 0.5, Height = 0.5 },
            _ => new ZoneRect { X = 0, Y = 0, Width = 1, Height = 1 },
        };

        private static void HideOverlay()
        {
            _overlay?.HideOverlay();
            _overlayScreen = null;
            _overlayIsMagnetic = false;
            _currentMagneticRegion = MagneticRegion.None;
        }

        // ΝΕΟ (στα πρότυπα του FancyWM): Ctrl+Alt+βελάκι μετακινεί το ΤΡΕΧΟΝ ενεργό παράθυρο στη
        // γειτονική ζώνη προς αυτή την κατεύθυνση, χωρίς να χρειάζεται καθόλου ποντίκι/Shift-drag.
        // Δουλεύει πάνω στο ΙΔΙΟ αποθηκευμένο DeskZones layout της τρέχουσας οθόνης — αν δεν
        // υπάρχει ζώνη προς αυτή την κατεύθυνση (π.χ. ήδη στην τελευταία στήλη), δεν κάνει τίποτα.
        public enum ZoneDirection { Left, Right, Up, Down }

        public static void MoveForegroundWindowToZone(ZoneDirection direction)
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (!IsCandidateWindow(hwnd) || !GetWindowRect(hwnd, out var wr)) return;

                var center = new Point((wr.Left + wr.Right) / 2, (wr.Top + wr.Bottom) / 2);
                var screen = Screen.FromPoint(center);
                var layout = ZoneLayoutStore.GetLayout(screen.DeviceName);
                if (layout.Zones.Count < 2) return;

                var zones = layout.Zones.Select(z => z.ToAbsolute(screen.WorkingArea)).ToList();
                int currentIndex = zones.FindIndex(r => r.Contains(center));
                if (currentIndex < 0)
                {
                    currentIndex = 0;
                    double best = double.MaxValue;
                    for (int i = 0; i < zones.Count; i++)
                    {
                        double d = Distance(center, CenterOf(zones[i]));
                        if (d < best) { best = d; currentIndex = i; }
                    }
                }
                var fromCenter = CenterOf(zones[currentIndex]);

                int bestIdx = -1; double bestScore = double.MaxValue;
                for (int i = 0; i < zones.Count; i++)
                {
                    if (i == currentIndex) continue;
                    var c = CenterOf(zones[i]);
                    double dx = c.X - fromCenter.X, dy = c.Y - fromCenter.Y;
                    bool matches = direction switch
                    {
                        ZoneDirection.Left => dx < -4,
                        ZoneDirection.Right => dx > 4,
                        ZoneDirection.Up => dy < -4,
                        ZoneDirection.Down => dy > 4,
                        _ => false
                    };
                    if (!matches) continue;
                    bool horizontal = direction is ZoneDirection.Left or ZoneDirection.Right;
                    double primary = Math.Abs(horizontal ? dx : dy);
                    double perpendicular = Math.Abs(horizontal ? dy : dx);
                    double score = primary + perpendicular * 0.5;
                    if (score < bestScore) { bestScore = score; bestIdx = i; }
                }
                if (bestIdx < 0) return;

                // Ένα maximized παράθυρο αγνοεί το SetWindowPos (μένει μεγιστοποιημένο) — πρώτα restore.
                if (IsZoomed(hwnd)) ShowWindow(hwnd, 9 /* SW_RESTORE */);
                var target = AdjustForInvisibleFrame(hwnd, zones[bestIdx]);
                SetWindowPos(hwnd, IntPtr.Zero, target.X, target.Y, target.Width, target.Height, SWP_NOZORDER | SWP_NOACTIVATE);
                ZoneOccupancy.Set(screen.DeviceName, bestIdx, hwnd);
            }
            catch { /* hotkey handler — ποτέ εξαίρεση προς τα έξω */ }
        }

        // ΝΕΟ ("πλήρες σετ πληκτρολογίου FancyWM", task tracker t27): μετακίνηση της ΕΣΤΙΑΣΗΣ στη
        // γειτονική ζώνη ΧΩΡΙΣ να μετακινηθεί κανένα παράθυρο — το αντίστοιχο FancyWM "focus
        // left/right/up/down". Λειτουργεί ακόμα και για παράθυρα που δεν έχουν ΠΟΤΕ γίνει snap από
        // το Opsis (ZoneOccupancy δεν τα ξέρει): fallback μέσω WindowFromPoint στο κέντρο της
        // γειτονικής ζώνης, ανεβαίνοντας στο πραγματικό top-level παράθυρο με GetAncestor.
        public static void FocusAdjacentZone(ZoneDirection direction)
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (!IsCandidateWindow(hwnd) || !GetWindowRect(hwnd, out var wr)) return;

                var center = new Point((wr.Left + wr.Right) / 2, (wr.Top + wr.Bottom) / 2);
                var screen = Screen.FromPoint(center);
                var layout = ZoneLayoutStore.GetLayout(screen.DeviceName);
                if (layout.Zones.Count < 2) return;

                var zones = layout.Zones.Select(z => z.ToAbsolute(screen.WorkingArea)).ToList();
                int currentIndex = zones.FindIndex(r => r.Contains(center));
                if (currentIndex < 0)
                {
                    currentIndex = 0;
                    double best = double.MaxValue;
                    for (int i = 0; i < zones.Count; i++)
                    {
                        double d = Distance(center, CenterOf(zones[i]));
                        if (d < best) { best = d; currentIndex = i; }
                    }
                }
                var fromCenter = CenterOf(zones[currentIndex]);

                int bestIdx = -1; double bestScore = double.MaxValue;
                for (int i = 0; i < zones.Count; i++)
                {
                    if (i == currentIndex) continue;
                    var c = CenterOf(zones[i]);
                    double dx = c.X - fromCenter.X, dy = c.Y - fromCenter.Y;
                    bool matches = direction switch
                    {
                        ZoneDirection.Left => dx < -4,
                        ZoneDirection.Right => dx > 4,
                        ZoneDirection.Up => dy < -4,
                        ZoneDirection.Down => dy > 4,
                        _ => false
                    };
                    if (!matches) continue;
                    bool horizontal = direction is ZoneDirection.Left or ZoneDirection.Right;
                    double primary = Math.Abs(horizontal ? dx : dy);
                    double perpendicular = Math.Abs(horizontal ? dy : dx);
                    double score = primary + perpendicular * 0.5;
                    if (score < bestScore) { bestScore = score; bestIdx = i; }
                }
                if (bestIdx < 0) return;

                IntPtr target = ZoneOccupancy.Get(screen.DeviceName, bestIdx) ?? IntPtr.Zero;
                if (target == IntPtr.Zero || !IsWindowVisible(target))
                {
                    var probe = CenterOf(zones[bestIdx]);
                    IntPtr atPoint = WindowFromPoint(new POINT { X = probe.X, Y = probe.Y });
                    target = atPoint == IntPtr.Zero ? IntPtr.Zero : GetAncestor(atPoint, GA_ROOT);
                }
                if (target != IntPtr.Zero && IsWindowVisible(target)) SetForegroundWindow(target);
            }
            catch { /* hotkey handler — ποτέ εξαίρεση προς τα έξω */ }
        }

        private static Point CenterOf(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
        private static double Distance(Point a, Point b) { double dx = a.X - b.X, dy = a.Y - b.Y; return Math.Sqrt(dx * dx + dy * dy); }

        // Ελαφρύ safety-net φίλτρο — το ίδιο το MOVESIZESTART event είναι ήδη αυθεντικό σήμα ότι
        // ΚΑΠΟΙΟ πραγματικό παράθυρο μπήκε σε move/size, οπότε δεν χρειάζονται πια οι αυστηροί
        // έλεγχοι στυλ (WS_CAPTION/WS_EX_TOOLWINDOW) της παλιάς υλοποίησης — αυτοί μπορούσαν να
        // απορρίψουν σιωπηλά ένα σύγχρονο παράθυρο με custom-drawn title bar. Κρατάμε μόνο τον
        // αποκλεισμό γνωστών shell/desktop κλάσεων ως δεύτερη γραμμή άμυνας.
        // Ορίζεται από το κύριο παράθυρο: επιστρέφει true για τα παράθυρα της δικής μας διεργασίας που ΕΠΙΤΡΕΠΕΤΑΙ να κουμπώνουν.
        public static Func<IntPtr, bool>? AllowOwnWindow { get; set; }

        // Applications the user excluded from snapping (Settings → DeskZones). Matched by process name, case-insensitive, ".exe" optional.
        private static bool IsExcludedApp(uint pid)
        {
            try
            {
                var excluded = MotionDesk.Services.AppSettings.Load().ZoneExcludedApps;
                if (excluded == null || excluded.Count == 0) return false;
                using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                var name = proc.ProcessName;
                return excluded.Any(e => string.Equals(System.IO.Path.GetFileNameWithoutExtension(e.Trim()), name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception) { return false; }   // process already gone / access denied -> treat as not excluded
        }

        private static bool IsCandidateWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd)) return false;
            // Παράθυρα που ζητούν ρητά να μην κουμπώνουν (π.χ. η μπάρα του News Crawler όταν τη μετακινεί ο χρήστης).
            if (GetProp(hwnd, "Opsis.NoSnap") != IntPtr.Zero) return false;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == (uint)Environment.ProcessId && AllowOwnWindow?.Invoke(hwnd) != true) return false;
            if (pid != (uint)Environment.ProcessId && IsExcludedApp(pid)) return false;

            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            string cls = sb.ToString();
            return cls is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "SysListView32");
        }

        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const uint GA_ROOT = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        private delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr hWnd, string name);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetClassName(IntPtr hwnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    }
}
