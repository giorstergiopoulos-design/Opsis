# Opsis 2.2.0

## Ελληνικά
- Νέα μηχανή DeskContainers (`OpsisContainers.exe`) με όλες τις ρυθμίσεις στο μενού ☰ ▸ Ρυθμίσεις κάθε DeskContainer· αυτόματη μεταφορά παλιών δεδομένων.
- Ένα εικονίδιο tray με ομαδοποιημένες επιλογές.
- Ασφάλεια: trust model plugins (έγκριση SHA-256), ασφαλής updater, έλεγχος διαδρομών εκκίνησης, ασφαλής αποσυμπίεση zip.
- Κοινή βιβλιοθήκη Opsis.UI για checkbox/slider/combobox.
- Εγκατάσταση για όλους τους χρήστες (Program Files)· αφαιρεί αυτόματα προηγούμενη εγκατάσταση ανά χρήστη.
- Υπόλοιπα για 2.2.1: δείτε το CHANGELOG.

## English
- New DeskContainers engine (`OpsisContainers.exe`); every setting lives in each container's ☰ ▸ Settings menu; old data is migrated automatically.
- A single grouped tray icon.
- Security: plugin trust model (SHA-256 approval), hardened updater, launch-path validation, safe zip extraction.
- Shared Opsis.UI controls (checkbox/slider/combobox).
- Machine-wide install into Program Files; an earlier per-user install is removed automatically.
- Open items for 2.2.1: see CHANGELOG.

## Assets / Αρχεία
| File | SHA-256 |
|---|---|
| OpsisSetup.exe | see `OpsisSetup.exe.sha256` |

Verify (PowerShell): `Get-FileHash OpsisSetup.exe -Algorithm SHA256`
