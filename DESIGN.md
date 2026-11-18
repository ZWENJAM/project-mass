# PROJECT MASS – Designsystem „Klar“

Entwürfe: Design-Board „Project Mass – Designrichtungen“ (Richtung A, Training aus B).

## Prinzipien
- Eine Hauptaktion pro Screen, in Daumenreichweite.
- Zahlen eintippen ist die Ausnahme: Gewicht und Wiederholungen per großer −/+-Tasten (2,5 kg, 1 Wdh.).
- Ruhige weiße Karten auf hellgrauem Grund, keine Deko.
- Live-Training ist ein dunkler Fokusmodus (`.focus-mode` schaltet die Farbvariablen um, Navigation verschwindet).

## Farben (CSS-Variablen in `app.css`)
| Token | Hell | Fokus | Bedeutung |
|---|---|---|---|
| --bg | #F2F3F5 | #15181C | Hintergrund |
| --surface | #FFFFFF | #1E2228 | Karten |
| --ink | #1B1F24 | #EEF1F4 | Text |
| --muted | #5E6670 | #A3ACB7 | Sekundärtext |
| --accent | #2457F5 | #3B6CFF | Hauptaktion, Protein, aktive Navigation |
| --success | #12A150 | #3CCB74 | erledigt, Ziel erreicht |
| --danger | #C62828 | – | Ziel überschritten, Löschen |
| --carb / --fat / --water | #8A93A0 / #F08C00 / #4AA3F0 | | Makro- und Wasserbalken |

## Typografie
Systemschrift (Roboto auf Android, Segoe auf Windows, SF auf Apple): nativ, offline, kein Download. Zahlen immer `tabular-nums`. Größen 13 / 15 / 16 / 19 / 30 / 44 px.

## Komponenten
- Buttons: Pille, primär blau gefüllt, sekundär hellgraue Fläche, Löschen als roter Text.
- Tabs: Segment-Schalter.
- Lebensmittel: Produktfoto (Open Food Facts) oder Kategorie-Piktogramm auf farbiger Kachel.
- Pausentimer: schwebende Leiste unten, nur sichtbar während einer Pause.
