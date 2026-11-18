# PROJECT MASS 2.0 – Spezifikation (Blazor WebAssembly PWA)

Ziel: Die bestehende Vanilla-JS-PWA (`_alt-pwa/`) wird als **Blazor WebAssembly PWA (.NET 8, Standalone)** neu gebaut und zur vollständigen Trainings- und Kalorien-Tracking-App erweitert. Sprache der UI: Deutsch. Zielgerät: Samsung (Chrome, installierbar), zusätzlich Desktop.

## Grundregeln
- .NET 8, `dotnet new blazorwasm --pwa`, Projektname `ProjectMass`. Solution `ProjectMass.sln` mit `src/ProjectMass` und `tests/ProjectMass.Tests` (xUnit).
- Kein Backend, keine Accounts. Daten lokal in **IndexedDB** (per kleinem JS-Interop-Modul `wwwroot/js/db.js`, keine schweren Fremdpakete nötig). Offline nutzbar.
- Architektur: `Models/`, `Services/` (Interfaces + Implementierungen, per DI registriert), `Pages/`, `Components/`. Geschäftslogik (Berechnungen) in Services, nicht in .razor-Dateien → testbar.
- Design: dunkles, sportliches Mobile-First-Design wie `_alt-pwa/style.css` (Farben/Typo übernehmen). Bottom-Navigation auf Mobile.
- Jede Phase muss mit `dotnet build` ohne Fehler und `dotnet test` grün abgeschlossen werden.

## Datenmodell (Kern)
- `UserProfile`: Name, Geschlecht, Geburtsjahr, Größe cm, Aktivitätslevel, Ziel (Aufbau/Halten/Diät), Ziel-kcal, Makroziele (Protein/KH/Fett g), Trainingstage/Woche.
- `Exercise`: Id, Name, Muskelgruppe(n), Equipment, Typ (Kraft/Cardio), Notiz. Mitgelieferte Startdatenbank (~60 Übungen, deutsch).
- `WorkoutPlan` → `PlannedExercise` (Übung, Sätze, Wdh.-Bereich, Pause s).
- `WorkoutSession` → `SessionExercise` → `SetEntry` (kg, Wdh., RPE optional, erledigt, Typ: Aufwärm/Arbeit/Drop).
- `FoodItem`: Id, Name, Marke, Barcode, Quelle (OFF/USDA/Eigen), kcal/Protein/KH/Fett/Ballaststoffe/Zucker/Salz **pro 100 g**, Portionsgrößen.
- `FoodLogEntry`: Datum, Mahlzeit (Frühstück/Mittag/Abend/Snack), FoodItem-Snapshot, Menge g → berechnete Nährwerte.
- `Recipe`: Name, Zutaten (FoodItem + g), Portionen → Nährwerte pro Portion.
- `BodyMeasurement`: Datum, Gewicht, Bauch, Brust, Oberarm, KFA.
- `WaterLog`, `Note`.

## Funktionen
### Training
- Pläne erstellen/bearbeiten aus Übungsdatenbank (Suche, Filter Muskelgruppe).
- Training live tracken: letzte Werte vorbelegt, Satz abhaken, Pausentimer (pro Übung konfigurierbar, Vibration bei Ende).
- **Progression**: Hinweis „Gewicht erhöhen“, wenn alle Arbeitssätze am oberen Wdh.-Ende geschafft wurden (+2,5 kg Oberkörper / +5 kg Unterkörper, konfigurierbar).
- Statistiken: geschätztes 1RM (Epley), persönliche Rekorde, Volumen pro Muskelgruppe/Woche, Trainingsfrequenz-Kalender.

### Ernährung
- Tagesansicht nach Mahlzeiten mit kcal + Makro-Ringen (Ist/Ziel).
- **Lebensmittelsuche über Open Food Facts** (`https://world.openfoodfacts.org/cgi/search.pl` bzw. API v2, `lc=de`, Felder einschränken) mit Debounce und Ergebnis-Cache in IndexedDB.
- **Barcode-Scanner** per Kamera (BarcodeDetector-API, Fallback ZXing-js via Interop) → `https://world.openfoodfacts.org/api/v2/product/{barcode}.json`.
- **USDA FoodData Central optional**: nur aktiv, wenn der Nutzer in den Einstellungen einen API-Key einträgt. Key nur lokal speichern.
- Eigene Lebensmittel anlegen, Favoriten, „Zuletzt gegessen“, Mahlzeit vom Vortag kopieren.
- Rezepte aus Zutaten zusammenstellen; alte Rezepte/Snacks aus `_alt-pwa/app.js` als Startdaten übernehmen.
- Wasser-Tracker.
- **Kalorienbedarf-Rechner**: Mifflin-St-Jeor × Aktivitätsfaktor, Zielanpassung (Aufbau +300, Diät −500), Makrovorschlag (Protein 2 g/kg, Fett 0,8 g/kg, Rest KH). Werte editierbar.
- Robuster API-Client: Timeout, Fehlermeldung offline, `User-Agent`-Hinweis gemäß OFF-Richtlinien (App-Name/Version), keine Massenanfragen.

### Körper & Fortschritt
- Messungen erfassen, Diagramme (Gewicht mit 7-Tage-Durchschnitt, Maße, KFA).
- Wochenbericht: Ø kcal/Protein vs. Ziel, Gewichtstrend, Trainings.

### Übersicht (Dashboard)
- Heute: kcal/Makros, nächstes Training, Wasser, Gewichtstrend, Streak.

### Daten
- JSON-Export/Import (inkl. Import des alten Backups `project-mass-v1` aus der Vanilla-PWA!), CSV-Export.
- Einstellungen: Profil, Ziele, Einheiten, USDA-Key, Daten löschen.

## Phasen (je Phase ein Auftrag, danach Review)
1. Gerüst: Solution, PWA, Layout/Navigation, Design, IndexedDB-Service, Models, Testprojekt.
2. Training: Übungsdatenbank, Pläne, Live-Tracking, Timer, Historie.
3. Ernährung Basis: Tageslog, Mahlzeiten, eigene Lebensmittel, Bedarfsrechner, Makroziele.
4. API: Open Food Facts Suche + Barcode-Scanner, Cache, optional USDA.
5. Fortschritt & Statistik: Messungen, Charts, 1RM/PRs/Volumen, Progression, Wochenbericht, Dashboard.
6. Daten & Feinschliff: Export/Import inkl. Alt-Backup, Rezepte, Wasser, Offline-Test, README.

## Abnahmekriterien je Phase
- `dotnet build` und `dotnet test` fehlerfrei.
- Unit-Tests für alle Berechnungen (Nährwerte pro Menge, Bedarfsrechner, 1RM, Progression, Volumen).
- Kurzer Bericht am Ende: was gebaut, welche Dateien, offene Punkte.
